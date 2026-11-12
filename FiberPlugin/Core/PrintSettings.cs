using System.IO;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Escolhas da janela Imprimir Folhas, gravadas no desenho (voltam preenchidas na próxima vez): um PDF com todas as
    /// folhas ou um por folha, nome do arquivo, pasta, cores e abrir o PDF no fim.
    /// </summary>
    public sealed class PrintSettings
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_IMPRESSAO";

        public bool SinglePdf { get; set; } = true;
        public bool Monochrome { get; set; }
        public bool OpenAfter { get; set; } = true;
        public string FileName { get; set; } = "";   // Sem ".pdf"; um PDF por folha: vai antes do nome da folha
        public string Folder { get; set; } = "";

        /// <summary>
        /// Arquivos que saem: um PDF só (nome.pdf) ou um por folha ("nome - FL-01.pdf"; sem nome, "FL-01.pdf"), na pasta.
        /// Cada item tem as folhas que vão nele, na ordem.
        /// </summary>
        public List<(string File, List<string> Sheets)> Outputs(IReadOnlyList<string> sheets)
        {
            if (SinglePdf) return new List<(string, List<string>)> { (Path.Combine(Folder, FileName + ".pdf"), sheets.ToList()) };
            return sheets.Select(s => (Path.Combine(Folder, (FileName.Length > 0 ? FileName + " - " : "") + s + ".pdf"), new List<string> { s }))
                .ToList();
        }

        /// <summary>As escolhas da última vez neste desenho; sem elas, PDF único com o nome do desenho, na pasta dele.</summary>
        public static PrintSettings Load(Database db, string drawingName, string drawingFolder)
        {
            var settings = new PrintSettings { FileName = drawingName, Folder = drawingFolder };
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            if (v == null || v.Length < 5) return settings;
            if (v[0].Value is int single) settings.SinglePdf = single != 0;
            if (v[1].Value is int mono) settings.Monochrome = mono != 0;
            if (v[2].Value is int open) settings.OpenAfter = open != 0;
            if (v[3].Value is string name && name.Length > 0) settings.FileName = name;
            if (v[4].Value is string folder && Directory.Exists(folder)) settings.Folder = folder;
            return settings;
        }

        public void Save(Transaction tr, Database db) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                new TypedValue((int)DxfCode.Int32, SinglePdf ? 1 : 0),
                new TypedValue((int)DxfCode.Int32, Monochrome ? 1 : 0),
                new TypedValue((int)DxfCode.Int32, OpenAfter ? 1 : 0),
                new TypedValue((int)DxfCode.Text, FileName),
                new TypedValue((int)DxfCode.Text, Folder));
    }
}
