using System.Collections.Specialized;
using System.Globalization;
using System.IO;
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
    /// Divide uma área do desenho em folhas (layouts) do tamanho e escala escolhidos, cada uma com moldura, carimbo
    /// (SheetFrame: logo, empresa, responsável técnico, assinatura...), marcas de dobra e de corte, e viewport travado
    /// mostrando um pedaço do projeto. As folhas já saem configuradas para DWG To PDF, prontas para o PUBLICAR do AutoCAD.
    /// A janela também apaga as folhas de um prefixo (botão Apagar folhas).
    /// </summary>
    public class SheetLayoutCommand
    {
        private const string PdfDevice = "DWG To PDF.pc3";
        private const string SheetIndexLayer = "FIBRA_FOLHAS";       // Quadro de articulação no Model (não imprime)
        private const string FrameLayer = "FIBRA_MOLDURA";           // Moldura e textos da folha
        private const string ViewportLayer = "FIBRA_VIEWPORTS";      // Borda do viewport (não imprime)

        [CommandMethod("FIBRA_GERAR_FOLHAS")]
        public void GenerateSheets()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Numa folha, o AutoCAD não deixa apagar a folha que está aberta, e a área é marcada no Model: avisa numa janela
            // (só na linha de comando passava despercebido)
            if (LayoutManager.Current.CurrentLayout != "Model")
            {
                const string notice = "Para gerar ou apagar as folhas, vá para a aba Model e use o Gerar Folhas de novo.";
                ed.WriteMessage($"\n[AVISO]: {notice}");
                System.Windows.Forms.MessageBox.Show(notice, "Fiber Plugin - Gerar Folhas",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            // O que tem no Model: as extensões de tudo (para a prévia e para descartar os pedaços vazios), a área da rede
            // e os símbolos da legenda
            List<Rect> content;
            Rect? network;
            SheetLegend legend;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                (content, network) = CollectContent(tr, modelSpace, DrawingScale.Factor(db));
                legend = SheetLegend.Collect(tr, modelSpace);
                tr.Commit();
            }

            // Tudo numa janela, com a prévia da divisão; as escolhas da última vez neste desenho voltam preenchidas
            SheetSettings options;
            SheetPlan plan;
            using (var form = new UI.SheetsForm(SheetSettings.Load(db), DrawingScale.Get(db), content, network,
                       prefix => LayoutsWithPrefix(db, prefix).Count))
            {
                form.PickInDrawing = () => Pick(ed, form);
                bool confirmed = AcApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
                if (confirmed && form.DeletePrefix is string deletePrefix)
                {
                    DeleteSheets(db, ed, deletePrefix);
                    return;
                }
                if (!confirmed || form.Result == null || form.Plan == null) return;
                options = form.Result;
                plan = form.Plan;
            }
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                options.Save(tr, db);
                tr.Commit();
            }

            string orientation = plan.Landscape ? "paisagem" : "retrato";
            ed.WriteMessage($"\n[INFO]: {plan.Tiles.Count} folha(s) {plan.Format.Name} {orientation}, escala 1:{plan.Scale} " +
                            $"(grade {plan.Columns} x {plan.Rows}, {plan.SkippedEmpty} pedaço(s) vazio(s) ignorado(s)).");

            // A janela já avisou que as folhas com o mesmo prefixo são substituídas
            List<string> existing = LayoutsWithPrefix(db, options.Prefix);

            int drawingScale = DrawingScale.Get(db);
            if (drawingScale != plan.Scale)
            {
                ed.WriteMessage($"\n[AVISO]: As anotações do desenho estão em 1:{drawingScale}. Para textos proporcionais a 1:{plan.Scale}, ajuste a escala em Configuração de Projeto.");
            }

            string title = Path.GetFileNameWithoutExtension(doc.Name);
            int digits = plan.Tiles.Count >= 100 ? 3 : 2;
            var names = new List<string?>(); // Nome de cada pedaço da grade (null = a folha não foi criada)
            var warnings = new HashSet<string>();

            // Legenda (carimbo): empresa e responsável técnico (CREA e e-mail) da aba Empresa; projeto, ART, endereço, município,
            // CEP, conteúdo, área e projetista das abas Projeto e Carimbo; a logo (e a assinatura, se houver) da pasta do memorial
            CompanyInfo company = CompanyInfo.Read();
            ProjectInfo project = ProjectInfo.Load(db);
            var images = new Dictionary<PaperImage, ObjectId>();
            double ImageAspect(PaperImage kind, string file, bool required)
            {
                if (ImageDefinition(db, file, required, warnings) is not (ObjectId id, double aspect)) return 0;
                images[kind] = id;
                return aspect;
            }
            bool engineer = company.Qualification.IndexOf("engenheir", StringComparison.OrdinalIgnoreCase) >= 0;
            var stamp = new SheetStamp
            {
                Company = company.LegalName,
                Engineer = (engineer && company.Representative.Length > 0 ? "ENG. " : "") + company.Representative.ToUpper(DataFiles.Br),
                Crea = company.Crea,
                EngineerEmail = company.RepresentativeEmail,
                Art = project.ArtNumber,
                Designer = project.Designer.ToUpper(DataFiles.Br),
                DesignerEmail = project.DesignerEmail,
                Title = project.Route.Length > 0 ? project.Route : title,
                Content = project.Content,
                Area = project.Area,
                Date = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Address = project.WorkAddress,
                City = project.City,
                ZipCode = project.ZipCode,
                Symbols = legend.Count,
                LogoAspect = ImageAspect(PaperImage.Logo, LogoFile, true),
                SignatureAspect = ImageAspect(PaperImage.Signature, SignatureFile, false)
            };
            if (company.IsEmpty || company.Representative.Length == 0)
                warnings.Add("Empresa ou responsável técnico em branco na legenda: preencha em Configuração de Projeto, aba Empresa, e gere as folhas de novo.");

            // Só com a legenda pronta: as folhas antigas saem logo antes de as novas serem criadas
            LayoutManager lm = LayoutManager.Current;
            DeleteLayouts(ed, lm, existing);

            // Não deixa o AutoCAD criar o viewport padrão nas folhas novas
            object? previousLcv = TrySetSystemVariable("LAYOUTCREATEVIEWPORT", (short)0);
            try
            {
                for (int i = 0; i < plan.Tiles.Count; i++)
                {
                    string name = $"{options.Prefix}-{(i + 1).ToString("D" + digits, CultureInfo.InvariantCulture)}";
                    try
                    {
                        SheetPaper paper = SheetFrame.Build(plan, i + 1, title, stamp);
                        string? warning = CreateSheet(db, lm, name, plan, plan.Tiles[i], paper, images, legend);
                        if (warning != null) warnings.Add(warning);
                        names.Add(name);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        // Ex.: folha antiga com esse nome que não pôde ser apagada; as outras saem assim mesmo
                        warnings.Add($"A folha {name} não foi criada ({ex.ErrorStatus}).");
                        names.Add(null);
                    }
                }
            }
            finally
            {
                if (previousLcv != null) TrySetSystemVariable("LAYOUTCREATEVIEWPORT", previousLcv);
                lm.CurrentLayout = "Model";
            }

            DrawSheetIndex(db, plan, options.Prefix, names);

            foreach (string warning in warnings) ed.WriteMessage($"\n[AVISO]: {warning}");
            List<string> created = names.OfType<string>().ToList();
            if (created.Count == 0) return;
            ed.WriteMessage($"\n[SUCESSO]: {created.Count} folha(s) criada(s): {created.First()} a {created.Last()}.");
            ed.WriteMessage("\n[DICA]: Os retângulos no Model (layer FIBRA_FOLHAS, não imprime) mostram a divisão. " +
                            "Para gerar o PDF, use o comando PUBLICAR e selecione as folhas.");
            ed.Regen();
        }

        /// <summary>
        /// Botão "Marcar no desenho" da janela: esconde a janela enquanto o usuário clica os dois cantos e devolve a área
        /// em coordenadas do mundo. Null se cancelar.
        /// </summary>
        private static Rect? Pick(Editor ed, System.Windows.Forms.Form form)
        {
            using (EditorUserInteraction interaction = ed.StartUserInteraction(form))
            {
                PromptPointResult p1 = ed.GetPoint("\nPrimeiro canto da área a dividir em folhas: ");
                if (p1.Status != PromptStatus.OK) return null;
                PromptPointResult p2 = ed.GetCorner("\nCanto oposto: ", p1.Value);
                if (p2.Status != PromptStatus.OK) return null;

                Matrix3d ucs = ed.CurrentUserCoordinateSystem;
                Point3d a = p1.Value.TransformBy(ucs), b = p2.Value.TransformBy(ucs);
                interaction.End();
                form.Focus();
                return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            }
        }

        /// <summary>
        /// Extensões dos elementos do Model, para descobrir quais folhas têm conteúdo, e a área da rede: onde há cabos,
        /// postes e caixas do plugin, com uma folga (10 m em 1:1000, na proporção da escala do desenho). Null sem rede.
        /// </summary>
        private static (List<Rect> Content, Rect? Network) CollectContent(Transaction tr, BlockTableRecord modelSpace, double scaleFactor)
        {
            var boxes = new List<Rect>();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                if (ent is Ray || ent is Xline) continue; // Infinitas
                if (ent.Layer.Equals(SheetIndexLayer, StringComparison.OrdinalIgnoreCase)) continue;

                Rect box;
                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    box = new Rect(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    continue; // Entidades sem extensão (texto vazio etc.)
                }
                boxes.Add(box);

                bool network = ent is Polyline && XDataTags.GetCableName(ent) != null ||
                               ent is BlockReference br && (XDataTags.ReadPole(br) != null || XDataTags.ReadBox(br) != null);
                if (!network) continue;
                minX = Math.Min(minX, box.MinX);
                minY = Math.Min(minY, box.MinY);
                maxX = Math.Max(maxX, box.MaxX);
                maxY = Math.Max(maxY, box.MaxY);
            }

            double margin = 10 * scaleFactor;
            Rect? area = minX <= maxX ? new Rect(minX - margin, minY - margin, maxX + margin, maxY + margin) : (Rect?)null;
            return (boxes, area);
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

        /// <summary>Botão Apagar folhas da janela: as folhas do prefixo e o quadro delas no Model.</summary>
        private static void DeleteSheets(Database db, Editor ed, string prefix)
        {
            List<string> names = LayoutsWithPrefix(db, prefix);
            DeleteLayouts(ed, LayoutManager.Current, names);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EraseSheetIndex(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite), prefix);
                tr.Commit();
            }
            int deleted = names.Count - LayoutsWithPrefix(db, prefix).Count;
            ed.WriteMessage($"\n[SUCESSO]: {deleted} folha(s) {prefix}-... apagada(s), junto com o quadro delas no Model.");
            ed.Regen();
        }

        // Imagens da legenda, na pasta do memorial (Dados\Memorial): a logo (a mesma do memorial) e, se houver, a assinatura
        private const string LogoFile = "logo.png", SignatureFile = "assinatura.png";

        /// <summary>
        /// Definição de uma imagem da legenda (uma por desenho, reaproveitada a cada geração) e a proporção dela. Null sem o
        /// arquivo: o quadro fica vazio (a falta da logo é avisada; a assinatura é opcional).
        /// </summary>
        private static (ObjectId Id, double Aspect)? ImageDefinition(Database db, string fileName, bool required, HashSet<string> warnings)
        {
            string? folder = PluginPaths.DataFile(MemorialDocument.AssetsFolder);
            string? file = folder == null ? null : Path.Combine(folder, fileName);
            if (file == null || !File.Exists(file))
            {
                if (required)
                    warnings.Add($"Sem a logo na legenda: coloque a imagem em Dados\\Memorial\\{fileName} (botão Pasta de Dados da Configuração de Projeto).");
                return null;
            }
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId dictionaryId = RasterImageDef.GetImageDictionary(db);
                    if (dictionaryId.IsNull) dictionaryId = RasterImageDef.CreateImageDictionary(db);
                    var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForWrite);
                    // foreach com o tipo, não Cast<DBDictionaryEntry>(): o enumerador "genérico" do dicionário devolve
                    // DictionaryEntry, e o Cast quebrava quando o desenho já tinha uma imagem (ex.: o mapa do Importar Ruas)
                    RasterImageDef? definition = null;
                    foreach (DBDictionaryEntry entry in dictionary)
                    {
                        if (tr.GetObject(entry.Value, OpenMode.ForRead) is RasterImageDef d &&
                            string.Equals(d.SourceFileName, file, StringComparison.OrdinalIgnoreCase))
                        {
                            definition = d;
                            break;
                        }
                    }
                    if (definition == null)
                    {
                        definition = new RasterImageDef { SourceFileName = file };
                        try
                        {
                            definition.Load();
                            dictionary.SetAt(RasterImageDef.SuggestName(dictionary, file), definition);
                        }
                        catch
                        {
                            definition.Dispose(); // Ainda fora do desenho: sem descartar, o AutoCAD pode cair mais tarde
                            throw;
                        }
                        tr.AddNewlyCreatedDBObject(definition, true);
                    }
                    else if (!definition.IsLoaded)
                    {
                        definition.UpgradeOpen();
                        definition.Load();
                    }
                    Vector2d size = definition.Size;
                    ObjectId id = definition.ObjectId;
                    tr.Commit();
                    return size.X > 0 && size.Y > 0 ? (id, size.X / size.Y) : null;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                warnings.Add($"A imagem {file} não pôde ser lida ({ex.ErrorStatus}): a legenda saiu sem ela.");
                return null;
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                // A imagem é só da legenda: um problema com ela não pode impedir as folhas de saírem
                warnings.Add($"A imagem {file} não pôde ser usada ({ex.Message}): a legenda saiu sem ela.");
                return null;
            }
        }

        /// <returns>Aviso sobre a configuração de impressão, ou null se tudo certo.</returns>
        private static string? CreateSheet(Database db, LayoutManager lm, string name, SheetPlan plan, SheetTile tile, SheetPaper paper,
            Dictionary<PaperImage, ObjectId> images, SheetLegend legend)
        {
            ObjectId layoutId = lm.CreateLayout(name);
            try
            {
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
                    CadHelpers.EnsureNonPlottingLayer(tr, db, ViewportLayer, 8);

                    DrawFrame(tr, paperSpace, paper, images);
                    legend.Draw(tr, db, paperSpace, paper.NorthArea, paper.SymbolCells, FrameLayer);

                    AddViewport(tr, paperSpace, plan.Format.ViewportArea(plan.Landscape), tile.ModelArea);
                    // Articulação: o desenho todo, pequeno, atrás do mapa das folhas
                    if (paper.Overview is (Rect onPaper, Rect shown)) AddViewport(tr, paperSpace, onPaper, shown);

                    tr.Commit();
                }
                return warning;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // A folha não saiu inteira (a transação desfez o desenho dela): sai também a folha, que senão ficava vazia
                // no desenho enquanto o aviso diz que ela não foi criada
                try
                {
                    lm.CurrentLayout = "Model";
                    lm.DeleteLayout(name);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Não deu para tirar: o próximo Gerar Folhas com o mesmo prefixo apaga
                }
                throw;
            }
        }

        /// <summary>Viewport travado em <paramref name="onPaper"/> (mm) mostrando <paramref name="shown"/> (metros), na mesma proporção.</summary>
        private static void AddViewport(Transaction tr, BlockTableRecord paperSpace, Rect onPaper, Rect shown)
        {
            Viewport vp = CadHelpers.Append(tr, paperSpace, new Viewport
            {
                CenterPoint = new Point3d(onPaper.CenterX, onPaper.CenterY, 0),
                Width = onPaper.Width,
                Height = onPaper.Height,
                Layer = ViewportLayer
            });

            vp.ViewDirection = Vector3d.ZAxis;
            vp.ViewTarget = Point3d.Origin;
            vp.ViewCenter = new Point2d(shown.CenterX, shown.CenterY);
            vp.CustomScale = onPaper.Width / shown.Width; // mm de papel por metro de desenho
            vp.On = true;
            vp.Locked = true;
        }

        /// <summary>Quadro, legenda, malha, notas, avisos e marcas (montados pelo SheetFrame), em mm no papel.</summary>
        private static void DrawFrame(Transaction tr, BlockTableRecord paperSpace, SheetPaper paper, Dictionary<PaperImage, ObjectId> images)
        {
            foreach (var (rect, width) in paper.Boxes)
            {
                Polyline box = Rectangle(rect);
                box.ConstantWidth = width;
                AddPaper(tr, paperSpace, box);
            }
            foreach (PaperLine l in paper.Lines)
            {
                var line = new Line(new Point3d(l.X1, l.Y1, 0), new Point3d(l.X2, l.Y2, 0));
                AddPaper(tr, paperSpace, line);
                if (l.Weight > 0) line.LineWeight = Weight(l.Weight);
            }
            foreach (Rect r in paper.Fills)
                AddPaper(tr, paperSpace, new Solid(new Point3d(r.MinX, r.MinY, 0), new Point3d(r.MaxX, r.MinY, 0),
                                                   new Point3d(r.MinX, r.MaxY, 0), new Point3d(r.MaxX, r.MaxY, 0)));
            foreach (PaperText t in paper.Texts)
            {
                AddPaper(tr, paperSpace, new MText
                {
                    Contents = CadHelpers.MTextLiteral(t.Text).Replace("\n", "\\P"),
                    Location = new Point3d(t.X, t.Y, 0),
                    Attachment = t.Anchor switch
                    {
                        PaperAnchor.BottomLeft => AttachmentPoint.BottomLeft,
                        PaperAnchor.MiddleLeft => AttachmentPoint.MiddleLeft,
                        PaperAnchor.MiddleCenter => AttachmentPoint.MiddleCenter,
                        PaperAnchor.MiddleRight => AttachmentPoint.MiddleRight,
                        _ => AttachmentPoint.TopLeft
                    },
                    TextHeight = t.Height,
                    Width = t.Width,
                    Rotation = t.Rotation
                });
            }

            foreach (var (area, kind) in paper.Images)
            {
                if (!images.TryGetValue(kind, out ObjectId definitionId)) continue;
                var definition = (RasterImageDef)tr.GetObject(definitionId, OpenMode.ForWrite);
                var image = new RasterImage
                {
                    ImageDefId = definitionId,
                    Orientation = new CoordinateSystem3d(new Point3d(area.MinX, area.MinY, 0), new Vector3d(area.Width, 0, 0), new Vector3d(0, area.Height, 0)),
                    ShowImage = true,
                    ImageTransparency = true // Fundo transparente do PNG
                };
                AddPaper(tr, paperSpace, image);
                RasterImage.EnableReactors(true);
                image.AssociateRasterDef(definition);
            }
        }

        /// <summary>Espessura de impressão mais próxima das do AutoCAD (0,18, 0,35, 0,70, 1,00 mm...).</summary>
        private static LineWeight Weight(double mm)
        {
            int hundredths = (int)Math.Round(mm * 100);
            return Enum.GetValues(typeof(LineWeight)).Cast<LineWeight>()
                .Where(w => (int)w >= 0)
                .OrderBy(w => Math.Abs((int)w - hundredths))
                .First();
        }

        private static void AddPaper(Transaction tr, BlockTableRecord paperSpace, Entity ent)
        {
            ent.Layer = FrameLayer;
            CadHelpers.Append(tr, paperSpace, ent);
        }

        private static Polyline Rectangle(Rect r)
        {
            var rect = new Polyline { Closed = true };
            rect.AddVertexAt(0, new Point2d(r.MinX, r.MinY), 0, 0, 0);
            rect.AddVertexAt(1, new Point2d(r.MaxX, r.MinY), 0, 0, 0);
            rect.AddVertexAt(2, new Point2d(r.MaxX, r.MaxY), 0, 0, 0);
            rect.AddVertexAt(3, new Point2d(r.MinX, r.MaxY), 0, 0, 0);
            return rect;
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
        private static void DrawSheetIndex(Database db, SheetPlan plan, string prefix, List<string?> names)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadHelpers.EnsureNonPlottingLayer(tr, db, SheetIndexLayer, 8);
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                EraseSheetIndex(tr, modelSpace, prefix);

                for (int i = 0; i < plan.Tiles.Count; i++)
                {
                    if (names[i] is not string name) continue;
                    Rect r = plan.Tiles[i].ModelArea;
                    Polyline rect = Rectangle(r);
                    rect.Layer = SheetIndexLayer;

                    var label = new MText
                    {
                        Contents = CadHelpers.MTextLiteral(name),
                        Location = new Point3d(r.MinX + r.Width * 0.02, r.MaxY - r.Width * 0.02, 0),
                        Attachment = AttachmentPoint.TopLeft,
                        TextHeight = Math.Min(r.Width, r.Height) * 0.05,
                        Layer = SheetIndexLayer
                    };

                    foreach (Entity ent in new Entity[] { rect, label })
                    {
                        CadHelpers.Append(tr, modelSpace, ent);
                        XDataTags.TagSheetIndex(tr, db, ent, prefix);
                    }
                }

                tr.Commit();
            }
        }

        /// <summary>Apaga do Model o quadro de articulação das folhas com o prefixo (coleta antes de apagar).</summary>
        private static void EraseSheetIndex(Transaction tr, BlockTableRecord modelSpace, string prefix)
        {
            CadHelpers.EraseUnlocked(tr, modelSpace.Cast<ObjectId>()
                .Where(id => tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                             string.Equals(XDataTags.GetSheetIndexPrefix(ent), prefix, StringComparison.OrdinalIgnoreCase))
                .ToList());
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
    }
}
