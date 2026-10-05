namespace FiberPlugin.Core
{
    /// <summary>
    /// Aparência de uma layer do plugin: cor (índice do AutoCAD, 1 a 255), tipo de linha (nome no acadiso.lin, null =
    /// contínua) e espessura em mm (null = a padrão do AutoCAD). Sem depender da API do AutoCAD; quem aplica na layer é
    /// CadHelpers.ApplyLayerStyle.
    /// </summary>
    public sealed class LayerStyle
    {
        public const string LinetypeFile = "acadiso.lin"; // Métrico: traços em mm no papel na escala 1:1000

        /// <summary>Tipos de linha oferecidos, com o nome em português (todos estão no acadiso.lin).</summary>
        public static readonly (string Name, string Label)[] Linetypes =
        {
            ("Continuous", "Contínua"),
            ("DASHED", "Tracejada"),
            ("DASHED2", "Tracejada curta"),
            ("DASHEDX2", "Tracejada longa"),
            ("HIDDEN", "Oculta"),
            ("DASHDOT", "Traço e ponto"),
            ("CENTER", "Centro"),
            ("PHANTOM", "Fantasma"),
            ("DOT", "Pontilhada"),
            ("DIVIDE", "Divisa"),
            ("BORDER", "Borda")
        };

        /// <summary>Espessuras de linha do AutoCAD, em mm.</summary>
        public static readonly double[] Weights =
            { 0, 0.05, 0.09, 0.13, 0.15, 0.18, 0.2, 0.25, 0.3, 0.35, 0.4, 0.5, 0.53, 0.6, 0.7, 0.8, 0.9, 1, 1.06, 1.2, 1.4, 1.58, 2, 2.11 };

        public const string DefaultWeightLabel = "Padrão";

        /// <summary>Caracteres que o AutoCAD não aceita em nomes de layer e bloco.</summary>
        public static readonly char[] InvalidNameChars = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

        public LayerStyle(short color, string? linetype = null, double? weightMm = null)
        {
            Color = color;
            Linetype = IsContinuous(linetype) ? null : linetype;
            WeightMm = weightMm;
        }

        public short Color { get; }
        public string? Linetype { get; }
        public double? WeightMm { get; }

        public bool SameAs(LayerStyle other) =>
            Color == other.Color && string.Equals(Linetype, other.Linetype, StringComparison.OrdinalIgnoreCase) && WeightMm == other.WeightMm;

        public static bool IsContinuous(string? linetype) =>
            string.IsNullOrWhiteSpace(linetype) || linetype!.Equals("Continuous", StringComparison.OrdinalIgnoreCase);

        /// <summary>"Tracejada" para "DASHED"; nome desconhecido aparece como está.</summary>
        public static string LinetypeLabel(string? name)
        {
            if (IsContinuous(name)) return Linetypes[0].Label;
            foreach (var (n, label) in Linetypes)
            {
                if (n.Equals(name, StringComparison.OrdinalIgnoreCase)) return label;
            }
            return name!;
        }

        /// <summary>"DASHED" para "Tracejada" (ou o próprio texto, se já for um nome); null = contínua.</summary>
        public static string? LinetypeFromLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            foreach (var (n, l) in Linetypes)
            {
                if (l.Equals(label!.Trim(), StringComparison.OrdinalIgnoreCase) || n.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase))
                    return IsContinuous(n) ? null : n;
            }
            return label!.Trim();
        }

        /// <summary>"0,25 mm", ou "Padrão" sem espessura própria.</summary>
        public static string WeightLabel(double? mm) =>
            mm is double w ? w.ToString("0.00", DataFiles.Br) + " mm" : DefaultWeightLabel;

        /// <summary>Espessura escrita ("0,25 mm", "0.25", "Padrão"): a da lista mais próxima; null = padrão ou inválida.</summary>
        public static double? WeightFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text!.Trim().Equals(DefaultWeightLabel, StringComparison.OrdinalIgnoreCase)) return null;
            if (!DataFiles.TryParseNumber(text.Replace("mm", ""), out double mm) || mm < 0) return null;
            return Weights.OrderBy(w => Math.Abs(w - mm)).First();
        }

        /// <summary>Layers do próprio AutoCAD: não podem ser renomeadas e mudar a aparência delas afeta o desenho todo.</summary>
        private static readonly string[] ReservedLayers = { "0", "Defpoints" };

        /// <summary>Problema do nome de layer (null se estiver certo).</summary>
        public static string? LayerNameError(string name)
        {
            if (name.Trim().Length == 0) return "informe o nome da layer";
            if (name.Length > 255) return "nome de layer muito longo";
            if (name.IndexOfAny(InvalidNameChars) >= 0) return "o nome da layer não pode ter < > / \\ \" : ; ? * | , = `";
            if (ReservedLayers.Any(r => r.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return $"a layer {name.Trim()} é do AutoCAD; use outro nome (ex.: RUAS)";
            return null;
        }
    }
}
