using System.Diagnostics;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Gera o Memorial Descritivo em PDF para a concessionária: capa, ofício, dados da empresa, percurso,
    /// cabos e postes levantados do desenho, e as figuras de instalação.
    /// Percurso e endereço da obra ficam gravados no DWG para a próxima vez.
    /// </summary>
    public class MemorialCommand
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_MEMORIAL";
        private static readonly CultureInfo Br = new CultureInfo("pt-BR");

        [CommandMethod("FIBRA_MEMORIAL")]
        public void Generate()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            CompanyInfo? company = CompanyInfo.Load(out string? error);
            if (company == null)
            {
                ed.WriteMessage($"\n[ERRO]: {error}");
                ed.WriteMessage("\n[DICA]: Use o botão Pasta de Dados e confira o arquivo empresa.txt.");
                return;
            }

            MemorialData data = Collect(db);
            data.Company = company;
            if (data.PoleCount == 0) ed.WriteMessage("\n[AVISO]: Nenhum poste no desenho.");
            if (data.Cables.Count == 0) ed.WriteMessage("\n[AVISO]: Nenhum cabo lançado no desenho.");

            // Dados do projeto (sugere os da última vez neste desenho)
            TypedValue[]? saved = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            string savedRoute = saved?.Length > 0 ? saved[0].Value as string ?? "" : "";
            string savedAddress = saved?.Length > 1 ? saved[1].Value as string ?? "" : "";
            string placeDate = Join(", ", company.City, DateTime.Today.ToString("d 'de' MMMM 'de' yyyy", Br));

            using (var form = new UI.MemorialForm(Summary(data), savedRoute, savedAddress, placeDate))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                data.Route = form.Route;
                data.WorkAddress = form.WorkAddress;
                data.PlaceAndDate = form.PlaceAndDate;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                    new TypedValue((int)DxfCode.Text, data.Route),
                    new TypedValue((int)DxfCode.Text, data.WorkAddress));
                tr.Commit();
            }

            string folder = Path.GetDirectoryName(doc.Name) is string dir && dir.Length > 0
                ? dir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fileName = "Memorial Descritivo - " + string.Join("-", data.Route.Split(Path.GetInvalidFileNameChars())) + ".pdf";
            string? pdf = CadHelpers.AskSavePath("Salvar Memorial Descritivo", Path.Combine(folder, fileName), "PDF (*.pdf)|*.pdf");
            if (pdf == null) return;

            ed.WriteMessage("\n[INFO]: Gerando o PDF...");
            string html = Path.Combine(Path.GetTempPath(), $"FiberPlugin-memorial-{Guid.NewGuid():N}.html");
            string? assets = PluginPaths.DataFile(MemorialDocument.AssetsFolder);
            try
            {
                File.WriteAllText(html, MemorialDocument.Html(data, assets), new System.Text.UTF8Encoding(false));
                error = PdfPrinter.Print(html, pdf);
            }
            finally
            {
                try { File.Delete(html); } catch (IOException) { }
            }

            if (error != null)
            {
                ed.WriteMessage($"\n[ERRO]: {error}");
                return;
            }

            ed.WriteMessage($"\n[SUCESSO]: Memorial salvo em {pdf}");
            try
            {
                Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true });
            }
            catch (System.Exception)
            {
                // Sem leitor de PDF associado: o arquivo continua salvo
            }
        }

        /// <summary>Postes (por tipo), CTO/CEO e cabos do Model.</summary>
        private static MemorialData Collect(Database db)
        {
            var data = new MemorialData();
            List<CableModel> catalog = CableProvider.GetCables();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);

                List<PoleInfo> poles = Poles.Collect(tr, modelSpace);
                data.PoleCount = poles.Count;
                data.PoleTypes = poles
                    .GroupBy(p => p.Data?.Designation ?? "Sem modelo")
                    .OrderByDescending(g => g.Count())
                    .Select(g =>
                    {
                        PoleData? m = g.First().Data;
                        string description = m == null
                            ? "Poste sem modelo definido (bloco antigo)"
                            : $"{m.TypeName} · {m.HeightM.ToString("0.#", Br)} m · {m.EffortDaN.ToString("0", Br)} daN";
                        return (g.Key, description, g.Count());
                    })
                    .ToList();

                List<BoxInfo> boxes = Boxes.Collect(tr, modelSpace);
                data.CtoCount = boxes.Count(b => b.Data.Kind == BlockCategories.Cto);
                data.CeoCount = boxes.Count(b => b.Data.Kind == BlockCategories.Ceo);

                data.Cables = CableDrawing.Totals(tr, modelSpace, catalog);
                tr.Commit();
            }
            return data;
        }

        /// <summary>"223 postes · 8.999 m de cabo · 18 CTO · 2 CEO" para o cabeçalho da janela.</summary>
        private static string Summary(MemorialData d) => Join(" · ",
            $"{d.PoleCount} poste(s)",
            $"{Math.Round(d.CableLength).ToString("N0", Br)} m de cabo",
            d.CtoCount > 0 ? $"{d.CtoCount} CTO" : "",
            d.CeoCount > 0 ? $"{d.CeoCount} CEO" : "");

        private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => p.Length > 0));
    }
}
