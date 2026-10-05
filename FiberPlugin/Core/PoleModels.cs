using System.IO;
using System.Text;
using Autodesk.AutoCAD.EditorInput;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Modelos de poste oferecidos no FIBRA_INSERIR_POSTE, lidos de Dados\postes.csv
    /// (colunas: Tipo;Altura_m;Esforco_daN, com tipo DT = Duplo T ou CC = Circular).
    /// Cada tipo usa o seu bloco do BLOCOS.dwg.
    /// </summary>
    public static class PoleModels
    {
        public const string FileName = "postes.csv";
        public const string Header = "Tipo;Altura_m;Esforco_daN";

        public static List<PoleData> Load(Editor? ed = null)
        {
            var warnings = new List<string>();
            List<PoleData> models = Read(warnings, out string? error);
            if (error != null) ed?.WriteMessage($"\n[ERRO]: {error}");
            foreach (string warning in warnings) ed?.WriteMessage($"\n[AVISO] {FileName}: {warning}");
            return models;
        }

        /// <summary>Modelos da planilha, sem depender do AutoCAD (linhas ignoradas em <paramref name="warnings"/>).</summary>
        public static List<PoleData> Read(List<string> warnings, out string? error)
        {
            error = null;
            var models = new List<PoleData>();
            string? path = PluginPaths.DataFile(FileName);
            if (path == null || !File.Exists(path))
            {
                error = $"Planilha de postes não encontrada ({path ?? PluginPaths.DataFolderName + "/" + FileName}).";
                return models;
            }

            try
            {
                foreach (var (lineNumber, cols) in DataFiles.ReadRows(path))
                {
                    if (cols.Length < 3) continue;

                    string type = cols[0].Trim().ToUpperInvariant();
                    bool validNumbers = DataFiles.TryParseNumber(cols[1], out double height) &
                                        DataFiles.TryParseNumber(cols[2], out double effort);

                    if (!validNumbers)
                    {
                        if (lineNumber > 1) warnings.Add($"linha {lineNumber} ignorada (altura ou esforço inválido).");
                        continue;
                    }
                    if (type != PoleData.DoubleT && type != PoleData.Circular)
                    {
                        warnings.Add($"linha {lineNumber} ignorada (tipo '{cols[0]}': use DT ou CC).");
                        continue;
                    }

                    models.Add(new PoleData { Type = type, HeightM = height, EffortDaN = effort });
                }
            }
            catch (IOException ex)
            {
                error = $"Não foi possível ler '{path}' ({ex.Message}).";
            }

            return models;
        }

        /// <summary>Conteúdo da planilha para estes modelos (números com vírgula, como o Excel em português).</summary>
        public static string ToCsv(IEnumerable<PoleData> models)
        {
            var sb = new StringBuilder(Header + "\r\n");
            foreach (PoleData m in models)
            {
                sb.Append(m.Type).Append(';')
                  .Append(m.HeightM.ToString("0.###", DataFiles.Br)).Append(';')
                  .Append(m.EffortDaN.ToString("0.###", DataFiles.Br)).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Grava a planilha de postes. Retorna o erro (null se gravou).</summary>
        public static string? Save(IEnumerable<PoleData> models) => DataFiles.Write(FileName, ToCsv(models));

        /// <summary>
        /// Bloco da biblioteca para o tipo de poste. Ordem de procura:
        /// nome exatamente "DT"/"CC"; nome com DT/CC como palavra (ex.: "POSTE DT"); "DUPLO T" / "CIRCULAR".
        /// </summary>
        public static string? BlockFor(string type, IEnumerable<string> blockNames)
        {
            List<string> names = blockNames.ToList();
            string fullName = type == PoleData.Circular ? "CIRCULAR" : "DUPLO";

            return names.FirstOrDefault(n => n.Trim().Equals(type, StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(n => BlockCategories.Words(n).Contains(type))
                ?? names.FirstOrDefault(n => BlockCategories.Words(n).Contains(fullName));
        }
    }
}
