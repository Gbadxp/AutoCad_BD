using System.Globalization;
using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Preferências do usuário para o que o plugin coloca sozinho no desenho: prefixos dos nomes (P-01, CTO-01,
    /// CEO-01), tamanhos na escala 1:1000, afastamento do roteamento automático e cores das layers. Valem para
    /// todos os desenhos e ficam em Documentos\Fiber Plugin\configuracoes.txt ("Campo: valor"), editado pela
    /// janela Configurações (FIBRA_CONFIGURACOES). Nada disso vai para o DWG.
    /// </summary>
    public sealed class UserSettings
    {
        public const string FileName = "configuracoes.txt";

        // Limites conferidos pela janela e na leitura do arquivo
        public const int MaxPrefixLength = 10;
        public const double MinTextHeight = 0.5, MaxTextHeight = 20;
        public const double MinBoxSymbol = 1, MaxBoxSymbol = 50;
        public const double MinArrowLength = 5, MaxArrowLength = 100;
        public const double MinRouteOffset = 0.3, MaxRouteOffset = 5;

        public string PolePrefix { get; set; } = "P-";
        public string CtoPrefix { get; set; } = "CTO-";
        public string CeoPrefix { get; set; } = "CEO-";
        public int NumberDigits { get; set; } = 2;

        /// <summary>Altura dos textos em mm no papel (escala 1:1000).</summary>
        public double TextHeight { get; set; } = 2.0;

        /// <summary>Maior lado do símbolo de CTO/CEO em mm no papel (escala 1:1000).</summary>
        public double BoxSymbolSize { get; set; } = 7.0;

        /// <summary>Comprimento da seta de esforço desenhada sem o bloco "SETA DE ESFORÇO", em mm no papel (1:1000).</summary>
        public double EffortArrowLength { get; set; } = 18.0;

        /// <summary>Afastamento do cabo em relação ao centro do poste no roteamento automático, em metros.</summary>
        public double AutoRouteOffset { get; set; } = 1.8;

        // Cores (índice de cor do AutoCAD, 1 a 255)
        public short CableColor { get; set; } = 3;       // Cabos sem cor própria na planilha: verde
        public short PoleLabelColor { get; set; } = 7;   // Textos dos postes: branco/preto
        public short BoxLabelColor { get; set; } = 7;    // Textos das CTO/CEO
        public short EffortColor { get; set; } = 4;      // Setas de esforço: ciano, como no modelo de projeto

        // Layer única das ruas do Importar Ruas (contorno, calçadas, eixos e nomes)
        public string RoadLayer { get; set; } = "RUAS";
        public short RoadColor { get; set; } = 8;        // Cinza: as ruas ficam de fundo e os cabos aparecem
        public string? RoadLinetype { get; set; }        // Null = contínua
        public double? RoadLineWeightMm { get; set; }    // Null = padrão do AutoCAD

        public LayerStyle RoadStyle => new LayerStyle(RoadColor, RoadLinetype, RoadLineWeightMm);

        /// <summary>Até que revisão os modelos de poste novos do padrão já entraram na lista do usuário (PoleModels.AddNewDefaults).</summary>
        public int PoleModelsRevision { get; set; }

        private static UserSettings? _current;

        public static string FilePath => Path.Combine(PluginPaths.UserRoot, FileName);

        /// <summary>Preferências em uso (lidas do arquivo na primeira vez e depois de cada Save/Reload).</summary>
        public static UserSettings Current => _current ??= Load();

        /// <summary>Relê o arquivo (ex.: ao abrir a janela Configurações, caso tenha sido editado à mão).</summary>
        public static void Reload() => _current = Load();

        public UserSettings Clone() => (UserSettings)MemberwiseClone();

        /// <summary>"P-07", "CTO-12"...: prefixo + número com os dígitos configurados.</summary>
        public string Name(string prefix, int number) => prefix + number.ToString("D" + NumberDigits, CultureInfo.InvariantCulture);

        public string BoxPrefix(string kind) => kind == BlockCategories.Ceo ? CeoPrefix : CtoPrefix;

        /// <summary>Problema do prefixo (null se estiver certo). Dígitos atrapalhariam a leitura do número de volta.</summary>
        public static string? PrefixError(string prefix)
        {
            if (prefix.Length > MaxPrefixLength) return $"no máximo {MaxPrefixLength} caracteres";
            if (prefix.Any(char.IsDigit)) return "sem números (o número vem depois do prefixo)";
            if (prefix.IndexOfAny(new[] { '\\', '{', '}', ';' }) >= 0) return "sem \\ { } ;";
            return null;
        }

        /// <summary>Primeiro problema das preferências, em português (null se estiver tudo certo).</summary>
        public string? Validate()
        {
            foreach (var (label, prefix) in new[] { ("dos postes", PolePrefix), ("das CTO", CtoPrefix), ("das CEO", CeoPrefix) })
            {
                if (PrefixError(prefix) is string error) return $"Prefixo {label}: {error}.";
            }
            if (string.Equals(CtoPrefix, CeoPrefix, StringComparison.OrdinalIgnoreCase)) return "Os prefixos das CTO e das CEO precisam ser diferentes.";
            if (NumberDigits < 1 || NumberDigits > 4) return "Use de 1 a 4 dígitos no número.";
            if (!InRange(TextHeight, MinTextHeight, MaxTextHeight)) return $"Altura dos textos: de {MinTextHeight:0.#} a {MaxTextHeight:0} mm.";
            if (!InRange(BoxSymbolSize, MinBoxSymbol, MaxBoxSymbol)) return $"Símbolo da CTO/CEO: de {MinBoxSymbol:0} a {MaxBoxSymbol:0} mm.";
            if (!InRange(EffortArrowLength, MinArrowLength, MaxArrowLength)) return $"Seta de esforço: de {MinArrowLength:0} a {MaxArrowLength:0} mm.";
            if (!InRange(AutoRouteOffset, MinRouteOffset, MaxRouteOffset)) return $"Afastamento do roteamento: de {MinRouteOffset:0.0} a {MaxRouteOffset:0} m.";
            if (LayerStyle.LayerNameError(RoadLayer) is string layerError) return $"Layer das ruas: {layerError}.";
            return null;
        }

        private static bool InRange(double value, double min, double max) => value >= min && value <= max;

        /// <summary>Lê o arquivo. Campo ausente ou fora dos limites fica com o padrão.</summary>
        public static UserSettings Load()
        {
            var settings = new UserSettings();
            Dictionary<string, string> fields;
            try
            {
                if (!File.Exists(FilePath)) return settings;
                fields = DataFiles.ReadFields(FilePath);
            }
            catch (IOException) { return settings; }
            catch (UnauthorizedAccessException) { return settings; }

            string? Text(string name) => fields.TryGetValue(DataFiles.FieldKey(name), out string? v) ? v : null;
            double Number(string name, double fallback, double min, double max) =>
                Text(name) is string v && DataFiles.TryParseNumber(v, out double n) && InRange(n, min, max) ? n : fallback;
            short Color(string name, short fallback) =>
                Text(name) is string v && short.TryParse(v.Trim(), out short c) && c >= 1 && c <= 255 ? c : fallback;
            string Prefix(string name, string fallback) =>
                Text(name) is string v && PrefixError(v) == null ? v : fallback;

            settings.PolePrefix = Prefix("Prefixo dos postes", settings.PolePrefix);
            settings.CtoPrefix = Prefix("Prefixo das CTO", settings.CtoPrefix);
            settings.CeoPrefix = Prefix("Prefixo das CEO", settings.CeoPrefix);
            if (string.Equals(settings.CtoPrefix, settings.CeoPrefix, StringComparison.OrdinalIgnoreCase))
            {
                settings.CtoPrefix = "CTO-";
                settings.CeoPrefix = "CEO-";
            }
            settings.NumberDigits = (int)Number("Digitos do numero", settings.NumberDigits, 1, 4);
            settings.TextHeight = Number("Altura dos textos (mm)", settings.TextHeight, MinTextHeight, MaxTextHeight);
            settings.BoxSymbolSize = Number("Simbolo da CTO/CEO (mm)", settings.BoxSymbolSize, MinBoxSymbol, MaxBoxSymbol);
            settings.EffortArrowLength = Number("Seta de esforco (mm)", settings.EffortArrowLength, MinArrowLength, MaxArrowLength);
            settings.AutoRouteOffset = Number("Afastamento do roteamento (m)", settings.AutoRouteOffset, MinRouteOffset, MaxRouteOffset);
            settings.CableColor = Color("Cor dos cabos", settings.CableColor);
            settings.PoleLabelColor = Color("Cor dos textos dos postes", settings.PoleLabelColor);
            settings.BoxLabelColor = Color("Cor dos textos das CTO/CEO", settings.BoxLabelColor);
            settings.EffortColor = Color("Cor das setas de esforco", settings.EffortColor);
            if (Text("Layer das ruas") is string road && LayerStyle.LayerNameError(road) == null) settings.RoadLayer = road.Trim();
            settings.RoadColor = Color("Cor das ruas", settings.RoadColor);
            settings.RoadLinetype = LayerStyle.LinetypeFromLabel(Text("Tipo de linha das ruas"));
            settings.RoadLineWeightMm = LayerStyle.WeightFromText(Text("Espessura das ruas (mm)"));
            settings.PoleModelsRevision = Text("Revisao dos modelos de poste") is string revision && int.TryParse(revision, out int r) && r >= 0 ? r : 0;
            return settings;
        }

        /// <summary>Grava o arquivo e passa a usar estas preferências. Retorna o erro (null se gravou).</summary>
        public string? Save()
        {
            string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("# Fiber Plugin - preferências (use o botão Configurações da aba Fibra para editar)");
            sb.AppendLine("# Tamanhos em mm no papel na escala 1:1000; cores pelo número de cor do AutoCAD (1 a 255).");
            sb.AppendLine($"Prefixo dos postes: {PolePrefix}");
            sb.AppendLine($"Prefixo das CTO: {CtoPrefix}");
            sb.AppendLine($"Prefixo das CEO: {CeoPrefix}");
            sb.AppendLine($"Digitos do numero: {NumberDigits}");
            sb.AppendLine($"Altura dos textos (mm): {N(TextHeight)}");
            sb.AppendLine($"Simbolo da CTO/CEO (mm): {N(BoxSymbolSize)}");
            sb.AppendLine($"Seta de esforco (mm): {N(EffortArrowLength)}");
            sb.AppendLine($"Afastamento do roteamento (m): {N(AutoRouteOffset)}");
            sb.AppendLine($"Cor dos cabos: {CableColor}");
            sb.AppendLine($"Cor dos textos dos postes: {PoleLabelColor}");
            sb.AppendLine($"Cor dos textos das CTO/CEO: {BoxLabelColor}");
            sb.AppendLine($"Cor das setas de esforco: {EffortColor}");
            sb.AppendLine($"Layer das ruas: {RoadLayer}");
            sb.AppendLine($"Cor das ruas: {RoadColor}");
            sb.AppendLine($"Tipo de linha das ruas: {RoadLinetype ?? "Continuous"}");
            sb.AppendLine($"Espessura das ruas (mm): {(RoadLineWeightMm is double w ? N(w) : LayerStyle.DefaultWeightLabel)}");
            sb.AppendLine($"Revisao dos modelos de poste: {PoleModelsRevision}");

            try
            {
                Directory.CreateDirectory(PluginPaths.UserRoot);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (IOException ex) { return ex.Message; }
            catch (UnauthorizedAccessException ex) { return ex.Message; }

            _current = Clone();
            return null;
        }
    }
}
