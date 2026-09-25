using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Divide uma área do desenho em folhas (layouts) do tamanho e escala escolhidos, cada uma com
    /// moldura, faixa de informações e viewport travado mostrando um pedaço do projeto.
    /// As folhas já saem configuradas para DWG To PDF, prontas para o PUBLICAR do AutoCAD.
    /// </summary>
    public class SheetLayoutCommand
    {
        private const string PdfDevice = "DWG To PDF.pc3";
        private const string SheetIndexLayer = "FIBRA_FOLHAS";       // Quadro de articulação no Model (não imprime)
        private const string FrameLayer = "FIBRA_MOLDURA";           // Moldura e textos da folha
        private const string ViewportLayer = "FIBRA_VIEWPORTS";      // Borda do viewport (não imprime)
        private const double PaperTextHeight = 3.5;                  // mm no papel

        private class SheetOptions
        {
            public Rect Area { get; set; }
            public SheetFormat Format { get; set; } = SheetFormat.All[2];
            public int Scale { get; set; }
            public double Overlap { get; set; }
            public string Prefix { get; set; } = "FL";
        }

        [CommandMethod("FIBRA_GERAR_FOLHAS")]
        public void GenerateSheets()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            if (LayoutManager.Current.CurrentLayout != "Model")
            {
                ed.WriteMessage("\n[AVISO]: Vá para a aba Model para marcar a área do projeto.");
                return;
            }

            SheetOptions? options = AskOptions(ed, db);
            if (options == null) return;

            // Planejamento: grade de folhas, melhor orientação, descartando folhas vazias
            List<Rect> content;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                content = CollectContent(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }

            SheetPlan plan = SheetPlanner.PlanBestOrientation(options.Area, options.Format, options.Scale, options.Overlap, content);
            if (plan.Tiles.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum elemento do desenho dentro da área escolhida.");
                return;
            }

            string orientation = plan.Landscape ? "paisagem" : "retrato";
            ed.WriteMessage($"\n[INFO]: {plan.Tiles.Count} folha(s) {plan.Format.Name} {orientation}, escala 1:{plan.Scale} " +
                            $"(grade {plan.Columns} x {plan.Rows}, {plan.SkippedEmpty} pedaço(s) vazio(s) ignorado(s)).");

            List<string> existing = LayoutsWithPrefix(db, options.Prefix);
            string question = existing.Count > 0
                ? $"\nSubstituir as {existing.Count} folha(s) \"{options.Prefix}-...\" que já existem? [Sim/Nao] <Sim>: "
                : "\nCriar as folhas? [Sim/Nao] <Sim>: ";
            if (!AskYes(ed, question))
            {
                ed.WriteMessage(existing.Count > 0 ? "\n[INFO]: Nada foi alterado. Use outro prefixo para manter as folhas atuais." : "\n[INFO]: Cancelado.");
                return;
            }

            int drawingScale = DrawingScale.Get(db);
            if (drawingScale != plan.Scale)
            {
                ed.WriteMessage($"\n[AVISO]: As anotações do desenho estão em 1:{drawingScale}. Para textos proporcionais a 1:{plan.Scale}, rode FIBRA_ESCALA.");
            }

            LayoutManager lm = LayoutManager.Current;
            DeleteLayouts(ed, lm, existing);

            string title = Path.GetFileNameWithoutExtension(doc.Name);
            int digits = plan.Tiles.Count >= 100 ? 3 : 2;
            var names = new List<string>();
            var warnings = new HashSet<string>();

            // Não deixa o AutoCAD criar o viewport padrão nas folhas novas
            object? previousLcv = TrySetSystemVariable("LAYOUTCREATEVIEWPORT", (short)0);
            try
            {
                for (int i = 0; i < plan.Tiles.Count; i++)
                {
                    string name = $"{options.Prefix}-{(i + 1).ToString("D" + digits, CultureInfo.InvariantCulture)}";
                    string? warning = CreateSheet(db, lm, name, plan, plan.Tiles[i], i + 1, title);
                    if (warning != null) warnings.Add(warning);
                    names.Add(name);
                }
            }
            finally
            {
                if (previousLcv != null) TrySetSystemVariable("LAYOUTCREATEVIEWPORT", previousLcv);
                lm.CurrentLayout = "Model";
            }

            DrawSheetIndex(db, plan, options.Prefix, names);

            foreach (string warning in warnings) ed.WriteMessage($"\n[AVISO]: {warning}");
            ed.WriteMessage($"\n[SUCESSO]: {names.Count} folha(s) criada(s): {names.First()} a {names.Last()}.");
            ed.WriteMessage("\n[DICA]: Os retângulos no Model (layer FIBRA_FOLHAS, não imprime) mostram a divisão. " +
                            "Para gerar o PDF, use o comando PUBLICAR e selecione as folhas.");
            ed.Regen();
        }

        private static SheetOptions? AskOptions(Editor ed, Database db)
        {
            // 1. Área
            PromptPointResult p1 = ed.GetPoint("\nPrimeiro canto da área a dividir em folhas: ");
            if (p1.Status != PromptStatus.OK) return null;
            PromptPointResult p2 = ed.GetCorner("\nCanto oposto: ", p1.Value);
            if (p2.Status != PromptStatus.OK) return null;

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d a = p1.Value.TransformBy(ucs);
            Point3d b = p2.Value.TransformBy(ucs);
            var area = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            if (area.Width < 1 || area.Height < 1)
            {
                ed.WriteMessage("\n[AVISO]: Área muito pequena.");
                return null;
            }

            // 2. Folha
            var pko = new PromptKeywordOptions("\nTamanho da folha [A0/A1/A2/A3/A4] <A2>: ", "A0 A1 A2 A3 A4") { AllowNone = true };
            PromptResult formatRes = ed.GetKeywords(pko);
            if (formatRes.Status == PromptStatus.Cancel) return null;
            SheetFormat format = SheetFormat.Find(formatRes.Status == PromptStatus.OK ? formatRes.StringResult : "A2") ?? SheetFormat.All[2];

            // 3. Escala (padrão: a escala do desenho)
            int drawingScale = DrawingScale.Get(db);
            var pio = new PromptIntegerOptions($"\nEscala 1:X <{drawingScale}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = DrawingScale.Min,
                UpperLimit = DrawingScale.Max
            };
            PromptIntegerResult scaleRes = ed.GetInteger(pio);
            if (scaleRes.Status == PromptStatus.Cancel) return null;
            int scale = scaleRes.Status == PromptStatus.OK ? scaleRes.Value : drawingScale;

            // 4. Sobreposição
            var pdo = new PromptDoubleOptions("\nSobreposição entre folhas vizinhas em % <5>: ")
            {
                AllowNone = true,
                AllowNegative = false
            };
            PromptDoubleResult overlapRes = ed.GetDouble(pdo);
            if (overlapRes.Status == PromptStatus.Cancel) return null;
            double overlap = Math.Min(50, overlapRes.Status == PromptStatus.OK ? overlapRes.Value : 5) / 100.0;

            // 5. Prefixo dos nomes
            var pso = new PromptStringOptions("\nPrefixo dos nomes das folhas <FL>: ") { AllowSpaces = false };
            PromptResult prefixRes = ed.GetString(pso);
            if (prefixRes.Status == PromptStatus.Cancel) return null;
            string prefix = CadHelpers.SanitizeName(prefixRes.StringResult.Trim());
            if (prefix.Length == 0) prefix = "FL";

            return new SheetOptions { Area = area, Format = format, Scale = scale, Overlap = overlap, Prefix = prefix };
        }

        /// <summary>Extensões dos elementos do Model, para descobrir quais folhas têm conteúdo.</summary>
        private static List<Rect> CollectContent(Transaction tr, BlockTableRecord modelSpace)
        {
            var boxes = new List<Rect>();
            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                if (ent is Ray || ent is Xline) continue; // Infinitas
                if (ent.Layer.Equals(SheetIndexLayer, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    boxes.Add(new Rect(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Entidades sem extensão (texto vazio etc.)
                }
            }
            return boxes;
        }

        private static List<string> LayoutsWithPrefix(Database db, string prefix)
        {
            var names = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    string rest = entry.Key.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)
                        ? entry.Key.Substring(prefix.Length + 1)
                        : "";
                    if (rest.Length > 0 && rest.All(char.IsDigit)) names.Add(entry.Key);
                }
            }
            return names;
        }

        private static void DeleteLayouts(Editor ed, LayoutManager lm, List<string> names)
        {
            foreach (string name in names)
            {
                try
                {
                    lm.DeleteLayout(name);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage($"\n[AVISO]: Não foi possível apagar a folha {name} ({ex.ErrorStatus}).");
                }
            }
        }

        /// <returns>Aviso sobre a configuração de impressão, ou null se tudo certo.</returns>
        private static string? CreateSheet(Database db, LayoutManager lm, string name, SheetPlan plan, SheetTile tile, int number, string title)
        {
            ObjectId layoutId = lm.CreateLayout(name);
            lm.CurrentLayout = name; // O viewport só pode ser ligado com a folha ativa

            string? warning;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForWrite);
                warning = ConfigurePlot(layout, plan);

                var paperSpace = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);

                // Remove viewports criados automaticamente (o primeiro é a própria folha e fica)
                ObjectIdCollection viewports = layout.GetViewports();
                for (int i = 1; i < viewports.Count; i++)
                {
                    tr.GetObject(viewports[i], OpenMode.ForWrite).Erase();
                }

                CadHelpers.EnsureLayer(tr, db, FrameLayer, 7);
                EnsureNonPlottingLayer(tr, db, ViewportLayer, 8);

                DrawFrame(tr, paperSpace, plan, number, title);

                Rect area = plan.Format.ViewportArea(plan.Landscape);
                var vp = new Viewport
                {
                    CenterPoint = new Point3d(area.CenterX, area.CenterY, 0),
                    Width = area.Width,
                    Height = area.Height,
                    Layer = ViewportLayer
                };
                paperSpace.AppendEntity(vp);
                tr.AddNewlyCreatedDBObject(vp, true);

                vp.ViewDirection = Vector3d.ZAxis;
                vp.ViewTarget = Point3d.Origin;
                vp.ViewCenter = new Point2d(tile.ModelArea.CenterX, tile.ModelArea.CenterY);
                vp.CustomScale = 1.0 / plan.MetersPerMm; // mm de papel por metro de desenho
                vp.On = true;
                vp.Locked = true;

                tr.Commit();
            }
            return warning;
        }

        /// <summary>Moldura, faixa de informações e textos, em mm no papel.</summary>
        private static void DrawFrame(Transaction tr, BlockTableRecord paperSpace, SheetPlan plan, int number, string title)
        {
            Rect frame = plan.Format.Frame(plan.Landscape);
            double bandTop = frame.MinY + SheetFormat.InfoBandHeight;

            var border = new Polyline();
            border.AddVertexAt(0, new Point2d(frame.MinX, frame.MinY), 0, 0, 0);
            border.AddVertexAt(1, new Point2d(frame.MaxX, frame.MinY), 0, 0, 0);
            border.AddVertexAt(2, new Point2d(frame.MaxX, frame.MaxY), 0, 0, 0);
            border.AddVertexAt(3, new Point2d(frame.MinX, frame.MaxY), 0, 0, 0);
            border.Closed = true;
            border.ConstantWidth = 0.5;
            AddPaper(tr, paperSpace, border);

            AddPaper(tr, paperSpace, new Line(new Point3d(frame.MinX, bandTop, 0), new Point3d(frame.MaxX, bandTop, 0)));

            double textY = frame.MinY + SheetFormat.InfoBandHeight / 2.0;
            string info = $"FOLHA {number:D2}/{plan.Tiles.Count:D2}     ESCALA 1:{plan.Scale}     {plan.Format.Name}";

            AddPaper(tr, paperSpace, new MText
            {
                Contents = title,
                Location = new Point3d(frame.MinX + 4, textY, 0),
                Attachment = AttachmentPoint.MiddleLeft,
                TextHeight = PaperTextHeight
            });
            AddPaper(tr, paperSpace, new MText
            {
                Contents = info,
                Location = new Point3d(frame.MaxX - 4, textY, 0),
                Attachment = AttachmentPoint.MiddleRight,
                TextHeight = PaperTextHeight
            });
        }

        private static void AddPaper(Transaction tr, BlockTableRecord paperSpace, Entity ent)
        {
            ent.Layer = FrameLayer;
            paperSpace.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        /// <summary>Impressora DWG To PDF, papel do formato escolhido, plotagem da folha em 1:1.</summary>
        private static string? ConfigurePlot(Layout layout, SheetPlan plan)
        {
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            try
            {
                psv.SetPlotConfigurationName(layout, PdfDevice, null);
                psv.RefreshLists(layout);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return $"Impressora \"{PdfDevice}\" não encontrada: ajuste a configuração de página das folhas manualmente.";
            }

            string w = plan.Format.PaperWidth(plan.Landscape).ToString("0.00", CultureInfo.InvariantCulture);
            string h = plan.Format.PaperHeight(plan.Landscape).ToString("0.00", CultureInfo.InvariantCulture);
            StringCollection media = psv.GetCanonicalMediaNameList(layout);

            // Prefere o papel "full bleed" (sem margem da impressora) já na orientação certa
            PlotRotation rotation = PlotRotation.Degrees000;
            string? paper = FindMedia(media, $"{w}_x_{h}");
            if (paper == null)
            {
                paper = FindMedia(media, $"{h}_x_{w}");
                rotation = PlotRotation.Degrees090;
            }
            if (paper == null)
            {
                return $"Papel {plan.Format.Name} não encontrado na impressora PDF: ajuste a configuração de página das folhas manualmente.";
            }

            psv.SetPlotConfigurationName(layout, PdfDevice, paper);
            psv.SetPlotPaperUnits(layout, PlotPaperUnit.Millimeters);
            psv.SetPlotRotation(layout, rotation);
            psv.SetPlotType(layout, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
            psv.SetUseStandardScale(layout, true);
            psv.SetStdScaleType(layout, StdScaleType.StdScale1To1);
            return null;
        }

        private static string? FindMedia(StringCollection media, string size)
        {
            return media.Cast<string>()
                .Where(m => m.IndexOf(size, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(m => m.IndexOf("full_bleed", StringComparison.OrdinalIgnoreCase) >= 0)
                .FirstOrDefault();
        }

        /// <summary>Quadro de articulação no Model: retângulo e nome de cada folha, numa layer que não imprime.</summary>
        private static void DrawSheetIndex(Database db, SheetPlan plan, string prefix, List<string> names)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureNonPlottingLayer(tr, db, SheetIndexLayer, 8);
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                // Remove o quadro anterior das folhas com o mesmo prefixo (coleta antes de apagar)
                var previous = new List<ObjectId>();
                foreach (ObjectId id in modelSpace)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                        string.Equals(XDataTags.GetSheetIndexPrefix(ent), prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        previous.Add(id);
                    }
                }
                foreach (ObjectId id in previous) tr.GetObject(id, OpenMode.ForWrite).Erase();

                for (int i = 0; i < plan.Tiles.Count; i++)
                {
                    Rect r = plan.Tiles[i].ModelArea;

                    var rect = new Polyline();
                    rect.AddVertexAt(0, new Point2d(r.MinX, r.MinY), 0, 0, 0);
                    rect.AddVertexAt(1, new Point2d(r.MaxX, r.MinY), 0, 0, 0);
                    rect.AddVertexAt(2, new Point2d(r.MaxX, r.MaxY), 0, 0, 0);
                    rect.AddVertexAt(3, new Point2d(r.MinX, r.MaxY), 0, 0, 0);
                    rect.Closed = true;
                    rect.Layer = SheetIndexLayer;
                    modelSpace.AppendEntity(rect);
                    tr.AddNewlyCreatedDBObject(rect, true);
                    XDataTags.TagSheetIndex(tr, db, rect, prefix);

                    var label = new MText
                    {
                        Contents = names[i],
                        Location = new Point3d(r.MinX + r.Width * 0.02, r.MaxY - r.Width * 0.02, 0),
                        Attachment = AttachmentPoint.TopLeft,
                        TextHeight = Math.Min(r.Width, r.Height) * 0.05,
                        Layer = SheetIndexLayer
                    };
                    modelSpace.AppendEntity(label);
                    tr.AddNewlyCreatedDBObject(label, true);
                    XDataTags.TagSheetIndex(tr, db, label, prefix);
                }

                tr.Commit();
            }
        }

        private static void EnsureNonPlottingLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            CadHelpers.EnsureLayer(tr, db, name, colorIndex);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
            if (ltr.IsPlottable)
            {
                ltr.UpgradeOpen();
                ltr.IsPlottable = false;
            }
        }

        /// <summary>Altera uma variável de sistema e devolve o valor anterior (null se ela não existir).</summary>
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

        private static bool AskYes(Editor ed, string message)
        {
            var pko = new PromptKeywordOptions(message, "Sim Nao") { AllowNone = true };
            PromptResult res = ed.GetKeywords(pko);
            return res.Status == PromptStatus.None || (res.Status == PromptStatus.OK && res.StringResult == "Sim");
        }
    }
}
