using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.EditorInput;
using FiberPlugin.Core;

namespace FiberPlugin.Models
{
    public class CableModel
    {
        public string FullName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public double WeightKgKm { get; set; }

        /// <summary>Número de fibras (coluna Fibras ou o nome: "06F.O", "36 FO"). Null se desconhecido.</summary>
        public int? Fibers { get; set; }

        /// <summary>Diâmetro externo em mm, do datasheet (coluna Diametro_mm). Null se não informado.</summary>
        public double? DiameterMm { get; set; }

        /// <summary>Cor da layer do cabo (coluna Cor, número de cor do AutoCAD de 1 a 255). Null = cor padrão dos cabos.</summary>
        public short? Color { get; set; }

        /// <summary>Tipo de linha da layer do cabo (nome no acadiso.lin, ex.: DASHED). Null = contínua.</summary>
        public string? Linetype { get; set; }

        /// <summary>Espessura da linha da layer do cabo, em mm. Null = a padrão do AutoCAD.</summary>
        public double? LineWeightMm { get; set; }

        /// <summary>Número de fibras escrito no nome do cabo ("ASU-80 06F.O" → 6, "CFOA-SM-AS-80-S-36 FO" → 36).</summary>
        public static int? FibersFromName(string name)
        {
            Match m = Regex.Match(name, @"(\d+)\s*F", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 ? n : (int?)null;
        }

        public override string ToString()
        {
            return FullName; // Para exibir no ComboBox do formulário
        }
    }

    /// <summary>
    /// Catálogo de cabos do plugin: Documentos\Fiber Plugin\cabos.txt, editado na janela Configurações (colunas
    /// NomeCompleto;NomeCurto;Peso_kg_km;Fibras;Diametro_mm;Cor;TipoLinha;Espessura_mm). Sem o arquivo, vale a planilha
    /// antiga Dados\cabos.csv (até a 1.9.35) ou, sem ela, a lista padrão do código. Relido a cada comando.
    /// </summary>
    public static class CableProvider
    {
        public const string FileName = "cabos.txt";
        public const string LegacyFileName = "cabos.csv";
        public const string Header = "NomeCompleto;NomeCurto;Peso_kg_km;Fibras;Diametro_mm;Cor;TipoLinha;Espessura_mm";

        /// <summary>Cabos que vêm com o plugin (autossustentados ASU-80, 120 e 200 de 6, 12 e 24 fibras).</summary>
        public static List<CableModel> Defaults()
        {
            var cables = new List<CableModel>();
            foreach (var (fibers, weights) in new[] { (6, new[] { 31.0, 34, 38 }), (12, new[] { 31.0, 34, 38 }), (24, new[] { 33.0, 46, 63 }) })
            {
                int[] spans = { 80, 120, 200 };
                for (int i = 0; i < spans.Length; i++)
                {
                    cables.Add(new CableModel
                    {
                        FullName = $"CFOA-SM-AS-{spans[i]}-S-{fibers:00} FO",
                        ShortName = $"ASU-{spans[i]} {fibers:00}F.O",
                        WeightKgKm = weights[i],
                        Fibers = fibers
                    });
                }
            }
            return cables;
        }

        /// <summary>Cabos do catálogo. Linhas com problema vão para o Editor como aviso.</summary>
        public static List<CableModel> GetCables(Editor? ed = null)
        {
            var warnings = new List<string>();
            List<CableModel> cables = Read(warnings, out string? error);
            if (error != null) ed?.WriteMessage($"\n[ERRO]: {error}");
            foreach (string warning in warnings) ed?.WriteMessage($"\n[AVISO] Cabos: {warning}");
            if (cables.Count == 0) ed?.WriteMessage("\n[ERRO]: Nenhum cabo cadastrado. Cadastre em Configurações > Cabos.");
            return cables;
        }

        /// <summary>
        /// Cabos do catálogo, sem depender do AutoCAD. <paramref name="warnings"/> recebe as linhas ignoradas;
        /// <paramref name="error"/>, o motivo de não ter lido o arquivo (aí vale a lista padrão).
        /// </summary>
        public static List<CableModel> Read(List<string> warnings, out string? error)
        {
            error = null;
            string? path = DataFiles.Source(FileName, LegacyFileName);
            if (path == null) return Defaults();

            try
            {
                return Parse(path, warnings);
            }
            catch (IOException ex)
            {
                error = $"Não foi possível ler '{path}' ({ex.Message}); usando os cabos padrão do plugin.";
                return Defaults();
            }
        }

        /// <summary>Conteúdo do arquivo para estes cabos (números com vírgula).</summary>
        public static string ToText(IEnumerable<CableModel> cables)
        {
            var sb = new StringBuilder(Header + "\r\n");
            foreach (CableModel c in cables)
            {
                sb.Append(string.Join(";",
                    c.FullName,
                    c.ShortName,
                    c.WeightKgKm.ToString("0.###", DataFiles.Br),
                    c.Fibers?.ToString(CultureInfo.InvariantCulture) ?? "",
                    c.DiameterMm?.ToString("0.###", DataFiles.Br) ?? "",
                    c.Color?.ToString(CultureInfo.InvariantCulture) ?? "",
                    c.Linetype ?? "",
                    c.LineWeightMm?.ToString("0.00", DataFiles.Br) ?? "")).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Grava o catálogo de cabos. Retorna o erro (null se gravou).</summary>
        public static string? Save(IEnumerable<CableModel> cables) => DataFiles.Save(FileName, ToText(cables), LegacyFileName);

        /// <summary>Avisa no Editor os cabos do desenho que não estão no catálogo (e foram ignorados nos cálculos).</summary>
        public static void ReportUnknown(Editor ed, IEnumerable<string> names)
        {
            foreach (string name in names) ed.WriteMessage($"\n[AVISO]: Cabo '{name}' não está cadastrado (Configurações > Cabos) e foi ignorado.");
        }

        public static CableModel? Find(IEnumerable<CableModel> cables, string? shortName)
        {
            if (shortName == null || shortName.Trim().Length == 0) return null;
            return cables.FirstOrDefault(c => c.ShortName.Equals(shortName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static List<CableModel> Parse(string path, List<string> errors)
        {
            var cables = new List<CableModel>();
            bool firstRow = true;

            foreach (var (lineNumber, cols) in DataFiles.ReadRows(path))
            {
                bool isFirst = firstRow;
                firstRow = false;

                if (cols.Length < 3)
                {
                    errors.Add($"linha {lineNumber} ignorada (esperado NomeCompleto;NomeCurto;Peso_kg_km).");
                    continue;
                }

                if (!DataFiles.TryParseNumber(cols[2], out double weight))
                {
                    // A primeira linha normalmente é o cabeçalho
                    if (!isFirst) errors.Add($"linha {lineNumber} ignorada (peso '{cols[2]}' inválido).");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cols[0]) || string.IsNullOrWhiteSpace(cols[1]) || weight <= 0)
                {
                    errors.Add($"linha {lineNumber} ignorada (nome vazio ou peso não positivo).");
                    continue;
                }

                if (Find(cables, cols[1]) != null)
                {
                    errors.Add($"linha {lineNumber} ignorada (nome curto '{cols[1]}' repetido).");
                    continue;
                }

                cables.Add(new CableModel
                {
                    FullName = cols[0],
                    ShortName = cols[1],
                    WeightKgKm = weight,
                    Fibers = cols.Length > 3 && int.TryParse(cols[3], out int fibers) && fibers > 0
                        ? fibers
                        : CableModel.FibersFromName(cols[1] + " " + cols[0]),
                    DiameterMm = cols.Length > 4 && DataFiles.TryParseNumber(cols[4], out double diameter) && diameter > 0
                        ? diameter
                        : (double?)null,
                    Color = cols.Length > 5 && short.TryParse(cols[5], out short color) && color >= 1 && color <= 255
                        ? color
                        : (short?)null,
                    Linetype = cols.Length > 6 ? LayerStyle.LinetypeFromLabel(cols[6]) : null,
                    LineWeightMm = cols.Length > 7 ? LayerStyle.WeightFromText(cols[7]) : null
                });
            }

            return cables;
        }
    }
}
