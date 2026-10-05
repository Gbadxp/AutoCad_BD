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
    /// Catálogo de cabos lido da planilha Dados\cabos.csv
    /// (colunas: NomeCompleto;NomeCurto;Peso_kg_km e, opcionais, Fibras;Diametro_mm;Cor).
    /// A planilha é relida a cada comando, então basta salvar no Excel (ou na janela Configurações) para o cabo
    /// novo aparecer.
    /// </summary>
    public static class CableProvider
    {
        public const string FileName = "cabos.csv";
        public const string Header = "NomeCompleto;NomeCurto;Peso_kg_km;Fibras;Diametro_mm;Cor";

        /// <summary>
        /// Cabos da planilha. Lista vazia (com aviso no Editor) se a planilha não existir ou não tiver
        /// nenhum cabo válido: a planilha é a única fonte dos cabos, não há lista paralela no código.
        /// </summary>
        public static List<CableModel> GetCables(Editor? ed = null)
        {
            var warnings = new List<string>();
            List<CableModel> cables = Read(warnings, out string? error);
            if (error != null)
            {
                ed?.WriteMessage($"\n[ERRO]: {error}");
                return cables;
            }
            foreach (string warning in warnings) ed?.WriteMessage($"\n[AVISO] {FileName}: {warning}");
            if (cables.Count == 0) ed?.WriteMessage($"\n[ERRO]: Nenhum cabo válido em '{PluginPaths.DataFile(FileName)}'.");
            return cables;
        }

        /// <summary>
        /// Cabos da planilha, sem depender do AutoCAD. <paramref name="warnings"/> recebe as linhas ignoradas;
        /// <paramref name="error"/>, o motivo de não ter lido nada (planilha ausente ou bloqueada).
        /// </summary>
        public static List<CableModel> Read(List<string> warnings, out string? error)
        {
            error = null;
            string? path = PluginPaths.DataFile(FileName);
            if (path == null || !File.Exists(path))
            {
                error = $"Planilha de cabos não encontrada ({path ?? PluginPaths.DataFolderName + "/" + FileName}). " +
                        "Use Dados do Projeto > Pasta de Dados para abrir a pasta.";
                return new List<CableModel>();
            }

            try
            {
                return Parse(path, warnings);
            }
            catch (IOException ex)
            {
                error = $"Não foi possível ler '{path}' ({ex.Message}).";
                return new List<CableModel>();
            }
        }

        /// <summary>Conteúdo da planilha para estes cabos (números com vírgula, como o Excel em português).</summary>
        public static string ToCsv(IEnumerable<CableModel> cables)
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
                    c.Color?.ToString(CultureInfo.InvariantCulture) ?? "")).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Grava a planilha de cabos. Retorna o erro (null se gravou).</summary>
        public static string? Save(IEnumerable<CableModel> cables) => DataFiles.Write(FileName, ToCsv(cables));

        /// <summary>Avisa no Editor os cabos do desenho que não estão na planilha (e foram ignorados nos cálculos).</summary>
        public static void ReportUnknown(Editor ed, IEnumerable<string> names)
        {
            foreach (string name in names) ed.WriteMessage($"\n[AVISO]: Cabo '{name}' não está na planilha de cabos e foi ignorado.");
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
                        : (short?)null
                });
            }

            return cables;
        }
    }
}
