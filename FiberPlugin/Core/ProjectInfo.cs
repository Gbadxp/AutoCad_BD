using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Dados do projeto usados nos documentos (Memorial Descritivo, Memorial de Esforço e Coordenadas dos Postes) e no
    /// carimbo das folhas do Gerar Folhas: percurso, endereço da obra, contrato de uso mútuo, ART, início e prazo da
    /// obra; município, CEP, conteúdo, área e o projetista com o e-mail (aba Carimbo). Ficam gravados no DWG e também em
    /// Documentos\Fiber Plugin\projeto.txt (os últimos salvos, fora do repositório): o campo que o desenho não tiver
    /// vem do arquivo, para não precisar digitar de novo a cada desenho.
    /// </summary>
    public sealed class ProjectInfo
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_MEMORIAL";
        public const string FileName = "projeto.txt";
        public const string Rural = "Rural", Urban = "Urbana";
        public const string DefaultContent = "PROJETO DE REDE ÓPTICA";

        // Mesma ordem do registro no DWG (gravado assim desde a 1.9.14; os do carimbo, no fim, desde a 1.9.90). Os null são
        // campos que saíram da legenda e ficam vazios no registro (e fora do projeto.txt): cliente, CPF/CNPJ do cliente, U.C.
        // e nº da obra (até a 1.9.93) e, nas posições 14 a 20, o quadro de alterações da 1.9.90 (desenhista, verificador,
        // aprovador, revisão), que não é lido. O projetista entrou depois, na 1.9.97
        private static readonly string?[] Labels =
        {
            "Percurso", "Endereço da obra", "Contrato de uso mútuo", "ART", "Início previsto", "Prazo de execução",
            null, null, "Município", "CEP", null, null, "Área do projeto", "Conteúdo",
            null, null, null, null, null, null, null, "Projetista", "E-mail do projetista"
        };

        public string Route { get; set; } = "";
        public string WorkAddress { get; set; } = "";
        public string ContractNumber { get; set; } = "";
        public string ArtNumber { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string Deadline { get; set; } = "";

        // Carimbo das folhas (aba Carimbo da Configuração de Projeto)
        public string City { get; set; } = "";            // Município
        public string ZipCode { get; set; } = "";
        public string Area { get; set; } = "";            // Rural, Urbana ou vazio
        public string Content { get; set; } = "";         // Conteúdo (ex.: PROJETO DE REDE ÓPTICA)
        public string Designer { get; set; } = "";        // Projetista (quem desenhou o projeto)
        public string DesignerEmail { get; set; } = "";

        /// <summary>
        /// Zona UTM: a do desenho ou, sem ela, a do último projeto salvo (só sugestão; quem grava no desenho é a
        /// janela Configuração de Projeto, que também atualiza os postes). Null se nenhum dos dois tiver.
        /// </summary>
        public UtmSettings? Zone { get; set; }

        /// <summary>True quando a zona veio do projeto.txt (o desenho ainda não tem zona).</summary>
        public bool ZoneFromFile { get; private set; }

        private const string ZoneLabel = "Zona UTM";

        /// <summary>Documentos\Fiber Plugin\projeto.txt.</summary>
        public static string FilePath => Path.Combine(PluginPaths.UserRoot, FileName);

        private string[] Values => new[]
        {
            Route, WorkAddress, ContractNumber, ArtNumber, StartDate, Deadline,
            "", "", City, ZipCode, "", "", Area, Content,
            "", "", "", "", "", "", "", Designer, DesignerEmail
        };

        /// <summary>Mesmos textos e mesma zona (para saber se algo mudou na aba Projeto).</summary>
        public bool SameAs(ProjectInfo other) =>
            Values.SequenceEqual(other.Values) &&
            (Zone == null ? other.Zone == null : other.Zone != null && Zone.Zone == other.Zone.Zone && Zone.South == other.Zone.South);

        private static ProjectInfo From(string[] v) => new ProjectInfo
        {
            Route = v[0], WorkAddress = v[1], ContractNumber = v[2], ArtNumber = v[3], StartDate = v[4], Deadline = v[5],
            City = v[8], ZipCode = v[9], Area = v[12], Content = v[13], Designer = v[21], DesignerEmail = v[22]
        };

        /// <summary>Uma cópia, para mudar só alguns campos e gravar sem perder os outros.</summary>
        public ProjectInfo Clone()
        {
            ProjectInfo copy = From(Values);
            copy.Zone = Zone;
            copy.ZoneFromFile = ZoneFromFile;
            return copy;
        }

        /// <summary>Os dados do desenho; o que ele não tiver vem do projeto.txt (os últimos salvos).</summary>
        public static ProjectInfo Load(Database db)
        {
            string[] drawing = ReadDrawing(db);
            Dictionary<string, string> file = ReadFile();

            ProjectInfo info = From(drawing.Select((value, i) => value.Length > 0 || Labels[i] is not string label ? value : FromFile(file, label)).ToArray());
            if (info.Content.Length == 0) info.Content = DefaultContent; // Sem nada gravado ainda
            info.Zone = UtmZone.Get(db);
            if (info.Zone == null && ParseZone(FromFile(file, ZoneLabel)) is UtmSettings saved)
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
                for (int i = 0; i < Labels.Length; i++)
                    if (Labels[i] != null) text.AppendLine($"{Labels[i]}: {Values[i]}");
                // Sem zona informada (ex.: salvo por um documento), mantém a que já estava no arquivo
                string zone = Zone != null ? $"{Zone.Zone} {(Zone.South ? "Sul" : "Norte")}" : FromFile(ReadFile(), ZoneLabel);
                text.AppendLine($"{ZoneLabel}: {zone}");
                Directory.CreateDirectory(PluginPaths.UserRoot);
                File.WriteAllText(FilePath, text.ToString(), new UTF8Encoding(true));
                return null;
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
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
            try
            {
                if (File.Exists(FilePath)) return DataFiles.ReadFields(FilePath);
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // Arquivo inacessível (permissão, sincronização da pasta): a janela só não vem preenchida
            }
            return new Dictionary<string, string>();
        }

        private static string FromFile(Dictionary<string, string> file, string label) =>
            file.TryGetValue(DataFiles.FieldKey(label), out string? value) ? value : "";
    }
}
