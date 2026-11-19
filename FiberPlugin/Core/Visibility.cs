using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// O que fica à vista no projeto (janela Visibilidade): os tipos de objeto do plugin e, pelo cadastro na Energisa,
    /// todos, só os cadastrados ou só os que faltam. O resto fica oculto (some da tela, das folhas e da impressão, mas
    /// continua no desenho).
    /// </summary>
    public sealed class VisibilityFilter
    {
        public const string Arrows = "Setas";
        public const int AllStatus = 0, DoneOnly = 1, PendingOnly = 2;
        private const string DictionaryKey = "FIBRA_PLUGIN_VISIBILIDADE";
        private static readonly string[] StatusCodes = { "Todos", "Feitos", "Faltam" };

        /// <summary>Tipos, na ordem da janela.</summary>
        public static readonly string[] Kinds =
            { Registration.Poles, Registration.Cto, Registration.Ceo, Registration.Cables, Arrows, Registration.Others };

        public HashSet<string> Shown { get; set; } = new HashSet<string>(Kinds, StringComparer.OrdinalIgnoreCase);
        public int Status { get; set; } = AllStatus;

        /// <summary>
        /// O objeto fica à vista: o tipo está marcado e o cadastro bate com o filtro. <paramref name="done"/> null = sem
        /// cadastro (seta de esforço sem poste): vale só o tipo.
        /// </summary>
        public bool Shows(string kind, bool? done) =>
            Shown.Contains(kind) && (done == null || Status == AllStatus || (Status == DoneOnly) == done.Value);

        public bool ShowsEverything => Status == AllStatus && Kinds.All(Shown.Contains);

        /// <summary>"Postes,CTO,Cabos|Faltam": como fica gravado no desenho e no arquivo dos presets.</summary>
        public string Code => string.Join(",", Kinds.Where(Shown.Contains)) + "|" + StatusCodes[Status];

        public static VisibilityFilter? Parse(string? code)
        {
            string[] parts = (code ?? "").Split('|');
            if (parts.Length != 2) return null;
            int status = Array.FindIndex(StatusCodes, s => s.Equals(parts[1].Trim(), StringComparison.OrdinalIgnoreCase));
            if (status < 0) return null;
            var shown = parts[0].Split(',').Select(k => k.Trim()).Where(k => Kinds.Contains(k, StringComparer.OrdinalIgnoreCase));
            return new VisibilityFilter { Shown = new HashSet<string>(shown, StringComparer.OrdinalIgnoreCase), Status = status };
        }

        /// <summary>"Postes, CTO e Cabos · só os que faltam cadastrar", para as mensagens.</summary>
        public string Summary
        {
            get
            {
                if (ShowsEverything) return "tudo à vista";
                string[] kinds = Kinds.Where(Shown.Contains).Select(Label).ToArray();
                string what = kinds.Length == 0 ? "nada" : kinds.Length == 1 ? kinds[0] : string.Join(", ", kinds.Take(kinds.Length - 1)) + " e " + kinds.Last();
                return what + (Status == DoneOnly ? " · só os cadastrados" : Status == PendingOnly ? " · só os que faltam cadastrar" : "");
            }
        }

        public static string Label(string kind) => kind == Arrows ? "Setas de esforço" : kind;

        /// <summary>O filtro aplicado por último neste desenho (tudo à vista, se nunca foi aplicado).</summary>
        public static VisibilityFilter Load(Database db)
        {
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            return v != null && v.Length > 0 && Parse(v[0].Value as string) is VisibilityFilter saved ? saved : new VisibilityFilter();
        }

        public void Save(Transaction tr, Database db) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey, new TypedValue((int)DxfCode.Text, Code));
    }

    /// <summary>Preset de visibilidade: um nome e o filtro.</summary>
    public sealed class VisibilityPreset
    {
        public string Name { get; set; } = "";
        public VisibilityFilter Filter { get; set; } = new VisibilityFilter();

        public override string ToString() => Name;
    }

    /// <summary>
    /// Presets da janela Visibilidade, em Documentos\Fiber Plugin\visibilidade.txt (valem para todos os desenhos), uma
    /// linha por preset: "Nome = Postes,CTO|Faltam". Sem o arquivo, alguns presets prontos.
    /// </summary>
    public static class VisibilityPresets
    {
        public const string FileName = "visibilidade.txt";

        public static List<VisibilityPreset> Load()
        {
            string path = DataFiles.UserFile(FileName);
            try
            {
                if (File.Exists(path))
                {
                    var presets = new List<VisibilityPreset>();
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        int equals = line.LastIndexOf('=');
                        if (line.Length == 0 || line.StartsWith("#") || equals <= 0) continue;
                        string name = line.Substring(0, equals).Trim();
                        if (name.Length > 0 && VisibilityFilter.Parse(line.Substring(equals + 1)) is VisibilityFilter filter &&
                            !presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            presets.Add(new VisibilityPreset { Name = name, Filter = filter });
                    }
                    return presets;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Arquivo inacessível: valem os prontos
            }
            return Defaults();
        }

        /// <summary>Grava os presets. Retorna o erro (null se gravou).</summary>
        public static string? Save(IEnumerable<VisibilityPreset> presets)
        {
            var text = new StringBuilder();
            text.AppendLine("# Presets da janela Visibilidade do Fiber Plugin: nome = tipos à vista | cadastro (Todos, Feitos ou Faltam).");
            text.AppendLine("# Tipos: " + string.Join(", ", VisibilityFilter.Kinds));
            foreach (VisibilityPreset p in presets) text.AppendLine($"{p.Name} = {p.Filter.Code}");
            return DataFiles.Save(FileName, text.ToString(), FileName);
        }

        private static List<VisibilityPreset> Defaults() => new List<VisibilityPreset>
        {
            new VisibilityPreset { Name = "Tudo", Filter = new VisibilityFilter() },
            Preset("Só CTO e CEO", "CTO,CEO|Todos"),
            Preset("Postes, CTO e CEO", "Postes,CTO,CEO|Todos"),
            Preset("Faltam cadastrar", string.Join(",", VisibilityFilter.Kinds) + "|Faltam"),
            Preset("Só cadastrados", string.Join(",", VisibilityFilter.Kinds) + "|Feitos")
        };

        private static VisibilityPreset Preset(string name, string code) =>
            new VisibilityPreset { Name = name, Filter = VisibilityFilter.Parse(code)! };
    }
}
