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
    /// Gera o Memorial Descritivo em PDF para a concessionária (conteúdo do item 16.2 da NDU 009): capa, ofício,
    /// dados da empresa e do contrato, cabos, postes e pontos de fixação, cálculo de esforços e figuras.
    /// Os dados do projeto digitados na janela ficam gravados no DWG para a próxima vez.
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

            ProjectData project = ProjectData.Collect(db, CableProvider.GetCables());
            MemorialData data = Build(db, project, company);
            if (data.PoleCount == 0) ed.WriteMessage("\n[AVISO]: Nenhum poste no desenho.");
            if (data.Cables.Count == 0) ed.WriteMessage("\n[AVISO]: Nenhum cabo lançado no desenho.");
            if (data.Efforts.Count == 0) ed.WriteMessage("\n[AVISO]: Nenhum esforço calculado: rode o Esforço no Percurso antes, para o memorial trazer a tabela de esforços.");

            // Dados do projeto (sugere os da última vez neste desenho)
            string[] saved = ReadSaved(db);
            string placeDate = Join(", ", company.City, DateTime.Today.ToString("d 'de' MMMM 'de' yyyy", Br));
            using (var form = new UI.MemorialForm(Summary(data), saved[0], saved[1], placeDate, saved[2], saved[3], saved[4], saved[5]))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                data.Route = form.Route;
                data.WorkAddress = form.WorkAddress;
                data.PlaceAndDate = form.PlaceAndDate;
                data.ContractNumber = form.ContractNumber;
                data.ArtNumber = form.ArtNumber;
                data.StartDate = form.StartDate;
                data.Deadline = form.Deadline;
            }
            if (data.ContractNumber.Length == 0) ed.WriteMessage("\n[AVISO]: Sem o número do contrato de uso mútuo, que a NDU 009 exige no memorial (item 16.2 a).");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                    new[] { data.Route, data.WorkAddress, data.ContractNumber, data.ArtNumber, data.StartDate, data.Deadline }
                        .Select(v => new TypedValue((int)DxfCode.Text, v)).ToArray());
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
            try
            {
                File.WriteAllText(html, MemorialDocument.Html(data, PluginPaths.DataFile(MemorialDocument.AssetsFolder)), new System.Text.UTF8Encoding(false));
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

        /// <summary>Converte o que foi levantado do desenho nos valores do memorial.</summary>
        private static MemorialData Build(Database db, ProjectData project, CompanyInfo company)
        {
            Traction traction = project.Traction;
            UtmSettings? utm = UtmZone.Get(db);

            return new MemorialData
            {
                Company = company,
                PoleCount = project.PoleList.Count,
                PoleTypes = project.PoleList
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
                    .ToList(),
                CtoCount = project.BoxList.Count(b => b.Data.Kind == BlockCategories.Cto),
                CeoCount = project.BoxList.Count(b => b.Data.Kind == BlockCategories.Ceo),
                FixationPoints = project.Occupied.Count,
                Cables = project.CableTotals.Select(t =>
                {
                    double maxSpan = project.MaxSpan(t.Name);
                    return new MemorialCable
                    {
                        Description = t.Description,
                        Name = t.Name,
                        Fibers = t.Model?.Fibers,
                        WeightKgKm = t.Model?.WeightKgKm ?? 0,
                        DiameterMm = t.Model?.DiameterMm,
                        Runs = t.Runs,
                        Length = t.Length,
                        MaxSpan = maxSpan,
                        MaxTension = t.Model != null ? traction.Tension(t.Model, maxSpan) : 0,
                        TractionSource = t.Model != null ? traction.Describe(t.Model) : "Cabo fora da planilha"
                    };
                }).ToList(),
                Efforts = project.Efforts.Select(e => new MemorialEffort
                {
                    Pole = e.Pole?.Number ?? "Sem poste",
                    Structure = e.Pole?.Data?.Designation ?? e.Pole?.Name ?? "",
                    Situation = e.Marker.Situation,
                    CableKgf = e.Marker.Kgf,
                    AngleDeg = e.Marker.AngleDeg,
                    TopKgf = e.Load.TopKgf,
                    ExistingKgf = e.Load.ExistingKgf,
                    TotalKgf = e.Load.TotalKgf,
                    NominalKgf = e.Load.NominalKgf,
                    Usage = e.Load.Usage,
                    Result = e.Load.Result
                }).ToList(),
                AttachHeightM = traction.Settings.AttachHeightM,
                TractionMethod = traction.UsesTable ? traction.Settings.MethodText : new CalcSettings { UseNormTable = false }.MethodText,
                CoordinateSystem = utm != null
                    ? $"UTM SIRGAS 2000, zona {utm.Zone} {(utm.South ? "Sul" : "Norte")}"
                    : "UTM SIRGAS 2000 (zona não definida no desenho)"
            };
        }

        /// <summary>Percurso, endereço, contrato, ART, início e prazo gravados da última vez (vazios se não houver).</summary>
        private static string[] ReadSaved(Database db)
        {
            TypedValue[]? values = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            var saved = new string[6];
            for (int i = 0; i < saved.Length; i++) saved[i] = values != null && values.Length > i ? values[i].Value as string ?? "" : "";
            return saved;
        }

        /// <summary>"223 postes · 8.999 m de cabo · 18 CTO · 2 CEO" para o cabeçalho da janela.</summary>
        private static string Summary(MemorialData d) => Join(" · ",
            $"{d.PoleCount} poste(s)",
            $"{Math.Round(d.CableLength).ToString("N0", Br)} m de cabo",
            d.CtoCount > 0 ? $"{d.CtoCount} CTO" : "",
            d.CeoCount > 0 ? $"{d.CeoCount} CEO" : "",
            $"{d.Efforts.Count} esforço(s)");

        private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => p.Length > 0));
    }
}
