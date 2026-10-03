using System.IO;
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
    /// (colunas: NomeCompleto;NomeCurto;Peso_kg_km e, opcionais, Fibras;Diametro_mm).
    /// A planilha é relida a cada comando, então basta salvar no Excel para o cabo novo aparecer.
    /// </summary>
    public static class CableProvider
    {
        public const string FileName = "cabos.csv";

        /// <summary>
        /// Cabos da planilha. Lista vazia (com aviso no Editor) se a planilha não existir ou não tiver
        /// nenhum cabo válido: a planilha é a única fonte dos cabos, não há lista paralela no código.
        /// </summary>
        public static List<CableModel> GetCables(Editor? ed = null)
        {
            string? path = PluginPaths.DataFile(FileName);
            if (path == null || !File.Exists(path))
            {
                ed?.WriteMessage($"\n[ERRO]: Planilha de cabos não encontrada ({path ?? PluginPaths.DataFolderName + "/" + FileName}). " +
                                 "Use Dados do Projeto > Pasta de Dados para abrir a pasta.");
                return new List<CableModel>();
            }

            try
            {
                var errors = new List<string>();
                List<CableModel> cables = Parse(path, errors);
                foreach (string error in errors) ed?.WriteMessage($"\n[AVISO] {FileName}: {error}");

                if (cables.Count == 0) ed?.WriteMessage($"\n[ERRO]: Nenhum cabo válido em '{path}'.");
                return cables;
            }
            catch (IOException ex)
            {
                ed?.WriteMessage($"\n[ERRO]: Não foi possível ler '{path}' ({ex.Message}).");
                return new List<CableModel>();
            }
        }

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
                        : (double?)null
                });
            }

            return cables;
        }
    }
}
