using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Imprimir Folhas: gera o PDF das folhas (layouts) escolhidas na janela, num arquivo só ou um por folha, pela
    /// impressora DWG To PDF do AutoCAD, cada folha com a configuração de página dela (as do Gerar Folhas já saem no
    /// papel certo). Folha configurada para outra impressora vai para o PDF no papel do mesmo tamanho.
    /// </summary>
    public class PrintCommand
    {
        [CommandMethod("FIBRA_IMPRIMIR_FOLHAS")]
        public void PrintSheets()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            var ed = doc.Editor;

            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
            {
                ed.WriteMessage("\n[AVISO]: O AutoCAD ainda está imprimindo outra coisa: espere terminar e tente de novo.");
                return;
            }

            // Folhas do desenho, na ordem das abas
            var sheets = new List<(string Name, string Detail, int Order)>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    if (tr.GetObject(entry.Value, OpenMode.ForRead) is not Layout layout || layout.ModelType) continue;
                    sheets.Add((layout.LayoutName, PaperText(layout), layout.TabOrder));
                }
            }
            sheets = sheets.OrderBy(s => s.Order).ToList();
            if (sheets.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: O desenho não tem folhas (layouts) para imprimir. Use o Gerar Folhas primeiro.");
                return;
            }

            // Já marcadas: as do prefixo do último Gerar Folhas, se houver; senão todas
            string prefix = SheetSettings.Load(db).Prefix + "-";
            var fromPlugin = new HashSet<string>(sheets.Select(s => s.Name).Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            var marked = fromPlugin.Count > 0 ? fromPlugin : new HashSet<string>(sheets.Select(s => s.Name));

            string drawingFolder = Path.IsPathRooted(doc.Name) && Directory.Exists(Path.GetDirectoryName(doc.Name))
                ? Path.GetDirectoryName(doc.Name)!
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            PrintSettings saved = PrintSettings.Load(db, Path.GetFileNameWithoutExtension(doc.Name), drawingFolder);

            PrintSettings settings;
            List<string> selected;
            using (var form = new UI.PrintForm(sheets.Select(s => (s.Name, s.Detail)).ToList(), marked, saved))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK || form.Result == null) return;
                settings = form.Result;
                selected = form.Selected;
            }
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                settings.Save(tr, db);
                tr.Commit();
            }

            try
            {
                Directory.CreateDirectory(settings.Folder);
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível criar a pasta {settings.Folder} ({ex.Message}).");
                return;
            }

            var warnings = new List<string>();
            var done = new List<string>();
            string previousLayout = LayoutManager.Current.CurrentLayout;
            object? previousBackground = TrySetSystemVariable("BACKGROUNDPLOT", (short)0); // Imprime na hora, sem segundo plano
            try
            {
                foreach (var (file, pages) in settings.Outputs(selected))
                {
                    // PDF aberto no leitor fica travado: avisa em vez de deixar a impressão falhar no meio
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        warnings.Add($"{Path.GetFileName(file)} não foi gravado: o arquivo está aberto (feche o PDF e imprima de novo).");
                        continue;
                    }
                    if (Plot(doc, pages, file, settings.Monochrome, warnings) && File.Exists(file)) done.Add(file);
                }
            }
            finally
            {
                if (previousBackground != null) TrySetSystemVariable("BACKGROUNDPLOT", previousBackground);
                try { LayoutManager.Current.CurrentLayout = previousLayout; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* Volta para onde estava; se não der, fica na última folha */ }
            }

            foreach (string warning in warnings) ed.WriteMessage($"\n[AVISO]: {warning}");
            if (done.Count == 0)
            {
                ed.WriteMessage("\n[ERRO]: Nenhum PDF foi gerado.");
                return;
            }
            ed.WriteMessage(done.Count == 1
                ? $"\n[SUCESSO]: PDF salvo em {done[0]}"
                : $"\n[SUCESSO]: {done.Count} PDFs salvos em {settings.Folder}");
            if (!settings.OpenAfter) return;
            try
            {
                Process.Start(new ProcessStartInfo(settings.SinglePdf ? done[0] : settings.Folder) { UseShellExecute = true });
            }
            catch (System.Exception)
            {
                // Sem leitor de PDF associado: os arquivos continuam salvos
            }
        }

        /// <summary>"594 × 420 mm" (papel da configuração de página da folha).</summary>
        private static string PaperText(Layout layout)
        {
            try
            {
                double w = layout.PlotPaperSize.X, h = layout.PlotPaperSize.Y;
                bool turned = layout.PlotRotation == PlotRotation.Degrees090 || layout.PlotRotation == PlotRotation.Degrees270;
                string size = turned ? $"{h:0} × {w:0} mm" : $"{w:0} × {h:0} mm";
                return layout.PlotConfigurationName.Equals(SheetLayoutCommand.PdfDevice, StringComparison.OrdinalIgnoreCase) ? size : size + " (outra impressora)";
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Imprime as folhas, na ordem, num PDF só (páginas uma atrás da outra). False se não saiu (o motivo vai para
        /// <paramref name="warnings"/>).
        /// </summary>
        private static bool Plot(Document doc, List<string> pages, string file, bool monochrome, List<string> warnings)
        {
            Database db = doc.Database;
            LayoutManager lm = LayoutManager.Current;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                using (PlotEngine engine = PlotFactory.CreatePublishEngine())
                using (var progress = new PlotProgressDialog(false, pages.Count, true))
                {
                    var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    progress.set_PlotMsgString(PlotMessageIndex.DialogTitle, "Fiber Plugin - Imprimir Folhas");
                    progress.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage, "Cancelar tudo");
                    progress.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage, "Cancelar a folha");
                    progress.set_PlotMsgString(PlotMessageIndex.SheetSetProgressCaption, "Folhas");
                    progress.set_PlotMsgString(PlotMessageIndex.SheetProgressCaption, "Folha");
                    progress.LowerPlotProgressRange = 0;
                    progress.UpperPlotProgressRange = 100;
                    progress.PlotProgressPos = 0;
                    progress.OnBeginPlot();
                    progress.IsVisible = true;
                    engine.BeginPlot(progress, null);

                    var settingsToDispose = new List<PlotSettings>();
                    try
                    {
                        for (int i = 0; i < pages.Count; i++)
                        {
                            var layout = (Layout)tr.GetObject(layouts.GetAt(pages[i]), OpenMode.ForRead);
                            lm.CurrentLayout = layout.LayoutName;

                            var settings = new PlotSettings(layout.ModelType);
                            settingsToDispose.Add(settings);
                            settings.CopyFrom(layout);
                            ToPdf(settings, layout);
                            if (monochrome) SetStyle(settings, db);

                            var info = new PlotInfo { Layout = layout.ObjectId, OverrideSettings = settings };
                            new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled }.Validate(info);

                            if (i == 0) engine.BeginDocument(info, doc.Name, null, 1, true, file);
                            progress.set_PlotMsgString(PlotMessageIndex.Status, $"{layout.LayoutName} ({i + 1} de {pages.Count})");
                            progress.OnBeginSheet();
                            progress.LowerSheetProgressRange = 0;
                            progress.UpperSheetProgressRange = 100;
                            progress.SheetProgressPos = 0;

                            engine.BeginPage(new PlotPageInfo(), info, i == pages.Count - 1, null);
                            engine.BeginGenerateGraphics(null);
                            progress.SheetProgressPos = 50;
                            engine.EndGenerateGraphics(null);
                            engine.EndPage(null);
                            progress.SheetProgressPos = 100;
                            progress.OnEndSheet();
                            progress.PlotProgressPos = (int)Math.Round(100.0 * (i + 1) / pages.Count);
                        }
                        engine.EndDocument(null);
                        progress.PlotProgressPos = 100;
                        progress.OnEndPlot();
                        engine.EndPlot(null);
                    }
                    finally
                    {
                        foreach (PlotSettings s in settingsToDispose) s.Dispose();
                    }
                    tr.Commit();
                }
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                warnings.Add($"{Path.GetFileName(file)} não foi gerado ({ex.Message}). Confira a configuração de página das folhas.");
                return false;
            }
        }

        /// <summary>
        /// Folha configurada para outra impressora: passa para a DWG To PDF no papel do mesmo tamanho (sem papel igual, fica
        /// como está e a impressão avisa).
        /// </summary>
        private static void ToPdf(PlotSettings settings, Layout layout)
        {
            if (layout.PlotConfigurationName.Equals(SheetLayoutCommand.PdfDevice, StringComparison.OrdinalIgnoreCase)) return;
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            double w = layout.PlotPaperSize.X, h = layout.PlotPaperSize.Y;
            psv.SetPlotConfigurationName(settings, SheetLayoutCommand.PdfDevice, null);
            psv.RefreshLists(settings);
            var media = psv.GetCanonicalMediaNameList(settings);
            string a = w.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            string b = h.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            string? paper = SheetLayoutCommand.FindMedia(media, $"{a}_x_{b}") ?? SheetLayoutCommand.FindMedia(media, $"{b}_x_{a}");
            if (paper != null) psv.SetCanonicalMediaName(settings, paper);
        }

        /// <summary>Preto e branco: a tabela de penas monochrome do AutoCAD (ctb ou stb, conforme o desenho).</summary>
        private static void SetStyle(PlotSettings settings, Database db)
        {
            try
            {
                PlotSettingsValidator.Current.SetCurrentStyleSheet(settings, db.PlotStyleMode ? "monochrome.ctb" : "monochrome.stb");
                settings.PlotPlotStyles = true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Sem a tabela monochrome na pasta de estilos: sai colorido
            }
        }

        private static object? TrySetSystemVariable(string name, object value)
        {
            try
            {
                object previous = AcApp.GetSystemVariable(name);
                AcApp.SetSystemVariable(name, value);
                return previous;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
