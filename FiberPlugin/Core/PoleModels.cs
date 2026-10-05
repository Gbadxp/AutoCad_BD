using System.IO;
using System.Text;
using Autodesk.AutoCAD.EditorInput;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Modelos de poste oferecidos no FIBRA_INSERIR_POSTE: Documentos\Fiber Plugin\postes.txt, editado na janela
    /// Configurações (colunas Tipo;Altura_m;Esforco_daN, com tipo DT = Duplo T ou CC = Circular). Sem o arquivo, vale a
    /// planilha antiga Dados\postes.csv (até a 1.9.35) ou, sem ela, a lista padrão do código.
    /// Cada tipo usa o seu bloco do BLOCOS.dwg.
    /// </summary>
    public static class PoleModels
    {
        public const string FileName = "postes.txt";
        public const string LegacyFileName = "postes.csv";
        public const string Header = "Tipo;Altura_m;Esforco_daN";

        /// <summary>Modelos que vêm com o plugin (tipo, altura em m, esforço em daN), por altura e esforço.</summary>
        private static readonly (string Type, double Height, double Effort)[] Standard =
        {
            ("DT", 9, 200), ("DT", 9, 400), ("DT", 9, 600),
            ("DT", 10, 200),
            ("DT", 11, 200), ("DT", 11, 300), ("CC", 11, 300), ("DT", 11, 400), ("DT", 11, 600), ("CC", 11, 600),
            ("DT", 11, 800), ("CC", 11, 800), ("DT", 11, 1000), ("CC", 11, 1000), ("DT", 11, 1500), ("CC", 11, 1500),
            ("DT", 12, 400), ("DT", 12, 600), ("CC", 12, 600), ("DT", 12, 800), ("CC", 12, 800),
            ("DT", 12, 1000), ("CC", 12, 1000), ("DT", 12, 1500), ("CC", 12, 1500),
            ("DT", 13, 1000), ("CC", 13, 1000), ("DT", 13, 1500), ("CC", 13, 1500)
        };

        /// <summary>
        /// Modelos que entraram no padrão depois que o usuário pode já ter a própria lista (revisão → modelos). Ao abrir o
        /// AutoCAD, AddNewDefaults acrescenta na lista dele os que ainda não estiverem lá, uma vez só por revisão (se ele
        /// apagar um depois, ele não volta).
        /// </summary>
        private static readonly (int Revision, (string Type, double Height, double Effort)[] Models)[] Additions =
        {
            (1, new[]
            {
                ("DT", 9.0, 400.0), ("DT", 9, 600), ("DT", 10, 200), ("DT", 11, 200), ("DT", 11, 400), ("DT", 11, 800), ("CC", 11, 800),
                ("DT", 12, 400), ("DT", 12, 600), ("CC", 12, 600), ("DT", 12, 800), ("CC", 12, 800),
                ("DT", 13, 1000), ("CC", 13, 1000), ("DT", 13, 1500), ("CC", 13, 1500)
            })
        };

        /// <summary>Revisão atual da lista padrão (a última de Additions).</summary>
        public static int DefaultsRevision => Additions.Max(a => a.Revision);

        private static PoleData Model((string Type, double Height, double Effort) m) =>
            new PoleData { Type = m.Type, HeightM = m.Height, EffortDaN = m.Effort };

        /// <summary>Modelos que vêm com o plugin: DT de 9 a 13 m e CC de 11 a 13 m, de 200 a 1500 daN.</summary>
        public static List<PoleData> Defaults() => Standard.Select(Model).ToList();

        /// <summary>
        /// Acrescenta na lista do usuário os modelos que entraram no padrão desde a última vez (UserSettings guarda a
        /// revisão já aplicada). Quem ainda usa a lista padrão já recebe os novos sem gravar nada. Retorna quantos entraram.
        /// </summary>
        public static int AddNewDefaults()
        {
            UserSettings settings = UserSettings.Current;
            if (settings.PoleModelsRevision >= DefaultsRevision) return 0;

            int added = 0;
            if (DataFiles.Source(FileName, LegacyFileName) != null)
            {
                List<PoleData> models = Read(new List<string>(), out string? error);
                if (error != null) return 0; // Tenta de novo na próxima abertura
                added = MergeAdditions(models, settings.PoleModelsRevision);
                if (added > 0 && Save(models) != null) return 0;
            }

            UserSettings updated = settings.Clone();
            updated.PoleModelsRevision = DefaultsRevision;
            updated.Save();
            return added;
        }

        /// <summary>
        /// Acrescenta no fim de <paramref name="models"/> os modelos das revisões depois de <paramref name="fromRevision"/>
        /// que ainda não estão lá. Retorna quantos entraram.
        /// </summary>
        public static int MergeAdditions(List<PoleData> models, int fromRevision)
        {
            int added = 0;
            foreach (var (_, newModels) in Additions.Where(a => a.Revision > fromRevision))
            {
                foreach (PoleData model in newModels.Select(Model))
                {
                    if (models.Any(m => m.SameModel(model))) continue;
                    models.Add(model);
                    added++;
                }
            }
            return added;
        }

        public static List<PoleData> Load(Editor? ed = null)
        {
            var warnings = new List<string>();
            List<PoleData> models = Read(warnings, out string? error);
            if (error != null) ed?.WriteMessage($"\n[ERRO]: {error}");
            foreach (string warning in warnings) ed?.WriteMessage($"\n[AVISO] Postes: {warning}");
            return models;
        }

        /// <summary>Modelos cadastrados, sem depender do AutoCAD (linhas ignoradas em <paramref name="warnings"/>).</summary>
        public static List<PoleData> Read(List<string> warnings, out string? error)
        {
            error = null;
            var models = new List<PoleData>();
            string? path = DataFiles.Source(FileName, LegacyFileName);
            if (path == null) return Defaults();

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
                error = $"Não foi possível ler '{path}' ({ex.Message}); usando os modelos padrão do plugin.";
                return Defaults();
            }

            return models;
        }

        /// <summary>Conteúdo do arquivo para estes modelos (números com vírgula).</summary>
        public static string ToText(IEnumerable<PoleData> models)
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

        /// <summary>Grava os modelos de poste. Retorna o erro (null se gravou).</summary>
        public static string? Save(IEnumerable<PoleData> models) => DataFiles.Save(FileName, ToText(models), LegacyFileName);

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
