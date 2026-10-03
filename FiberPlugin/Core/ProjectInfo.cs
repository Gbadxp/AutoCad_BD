using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Dados do projeto usados nos documentos (Memorial Descritivo, Memorial de Esforço e Coordenadas dos Postes):
    /// percurso, endereço da obra, contrato de uso mútuo, ART, início e prazo da obra. Ficam gravados no DWG e
    /// também em Documentos\Fiber Plugin\projeto.txt (os últimos salvos, fora do repositório): o campo que o desenho
    /// não tiver vem do arquivo, para não precisar digitar de novo a cada desenho.
    /// </summary>
    public sealed class ProjectInfo
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_MEMORIAL";
        public const string FileName = "projeto.txt";

        // Mesma ordem do registro no DWG (gravado assim desde a 1.9.14)
        private static readonly string[] Labels =
            { "Percurso", "Endereço da obra", "Contrato de uso mútuo", "ART", "Início previsto", "Prazo de execução" };

        public string Route { get; set; } = "";
        public string WorkAddress { get; set; } = "";
        public string ContractNumber { get; set; } = "";
        public string ArtNumber { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string Deadline { get; set; } = "";

        /// <summary>
        /// Zona UTM: a do desenho ou, sem ela, a do último projeto salvo (só sugestão; quem grava no desenho é a
        /// janela Dados do Projeto, que também atualiza os postes). Null se nenhum dos dois tiver.
        /// </summary>
        public UtmSettings? Zone { get; set; }

        /// <summary>True quando a zona veio do projeto.txt (o desenho ainda não tem zona).</summary>
        public bool ZoneFromFile { get; private set; }

        private const string ZoneLabel = "Zona UTM";

        /// <summary>Documentos\Fiber Plugin\projeto.txt.</summary>
        public static string FilePath => Path.Combine(PluginPaths.UserRoot, FileName);

        private string[] Values => new[] { Route, WorkAddress, ContractNumber, ArtNumber, StartDate, Deadline };

        private static ProjectInfo From(string[] v) => new ProjectInfo
        {
            Route = v[0], WorkAddress = v[1], ContractNumber = v[2], ArtNumber = v[3], StartDate = v[4], Deadline = v[5]
        };

        /// <summary>Os dados do desenho; o que ele não tiver vem do projeto.txt (os últimos salvos).</summary>
        public static ProjectInfo Load(Database db)
        {
            string[] drawing = ReadDrawing(db);
            Dictionary<string, string> file = ReadFile();
            string FromFile(string label) => file.TryGetValue(CompanyInfo.Key(label), out string? value) ? value : "";

            ProjectInfo info = From(drawing.Select((value, i) => value.Length > 0 ? value : FromFile(Labels[i])).ToArray());
            info.Zone = UtmZone.Get(db);
            if (info.Zone == null && ParseZone(FromFile(ZoneLabel)) is UtmSettings saved)
            {
                info.Zone = saved;
                info.ZoneFromFile = true;
            }
            return info;
        }

        /// <summary>"20 Sul", "20S", "23 Norte" → zona e hemisfério. Null se não for uma zona válida.</summary>
        private static UtmSettings? ParseZone(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(\d{1,2})\s*(S|N|Sul|Norte)?\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int zone) || zone < 1 || zone > 60) return null;
            return new UtmSettings { Zone = zone, South = !m.Groups[2].Value.StartsWith("N", StringComparison.OrdinalIgnoreCase) };
        }

        /// <summary>
        /// Grava no DWG e no projeto.txt. Retorna o motivo se o arquivo não pôde ser gravado (o DWG sempre é).
        /// </summary>
        public string? Save(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey, Values.Select(v => new TypedValue((int)DxfCode.Text, v)).ToArray());
                tr.Commit();
            }

            try
            {
                var text = new StringBuilder();
                text.AppendLine("# Dados do projeto do Fiber Plugin (os últimos salvos). Preenchem a janela dos documentos");
                text.AppendLine("# nos desenhos que ainda não têm esses dados. Pode editar aqui, um campo por linha.");
                for (int i = 0; i < Labels.Length; i++) text.AppendLine($"{Labels[i]}: {Values[i]}");
                // Sem zona informada (ex.: salvo por um documento), mantém a que já estava no arquivo
                string zone = Zone != null
                    ? $"{Zone.Zone} {(Zone.South ? "Sul" : "Norte")}"
                    : ReadFile().TryGetValue(CompanyInfo.Key(ZoneLabel), out string? kept) ? kept : "";
                text.AppendLine($"{ZoneLabel}: {zone}");
                Directory.CreateDirectory(PluginPaths.UserRoot);
                File.WriteAllText(FilePath, text.ToString(), new UTF8Encoding(true));
                return null;
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }

        private static string[] ReadDrawing(Database db)
        {
            TypedValue[]? values = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            return Labels.Select((_, i) => values != null && values.Length > i ? values[i].Value as string ?? "" : "").ToArray();
        }

        /// <summary>Campos do projeto.txt ("Campo: valor"), pelo nome sem acentos; vazio se o arquivo não existir.</summary>
        private static Dictionary<string, string> ReadFile()
        {
            var fields = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string raw in DataFiles.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        int colon = line.IndexOf(':');
                        if (line.Length == 0 || line.StartsWith("#") || colon <= 0) continue;
                        fields[CompanyInfo.Key(line.Substring(0, colon))] = line.Substring(colon + 1).Trim();
                    }
                }
            }
            catch (IOException)
            {
                // Sem o arquivo, a janela só não vem preenchida
            }
            return fields;
        }
    }
}
