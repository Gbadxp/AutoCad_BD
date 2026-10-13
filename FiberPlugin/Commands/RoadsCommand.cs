using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>Ruas do OpenStreetMap desenhadas na posição UTM do projeto.</summary>
    public class RoadsCommand
    {
        private const double Margin = 30;      // m além da borda: a rua chega inteira até o corte
        private const double NameScale = 1.25;   // Nomes com 2,5 mm no papel (o texto padrão do plugin tem 2 mm)
        private const double NameSpacing = 400;  // m entre repetições do nome numa rua longa, na escala 1:1000

        /// <summary>
        /// Abre a janela da área (retângulo pelos quatro lados ou centro e raio, em lat/long ou UTM, ou marcada no desenho),
        /// baixa as ruas do OpenStreetMap e desenha
        /// o contorno já unido (meio-fio, sem sobreposição nos cruzamentos), o eixo ou os dois, e os nomes, tudo na layer
        /// das ruas (RUAS por padrão; nome, cor, tipo de linha e espessura no botão Configurações).
        /// As ruas são cortadas na borda da área e ficam na zona UTM do projeto, alinhadas com postes e KML; o desenho
        /// recebe a geolocalização do AutoCAD se ainda não tiver. Se a área já tem ruas no desenho, dá para escrever só os
        /// nomes. Opcional: mapa de fundo do AutoCAD (satélite, mapa ou os dois), só na tela ou gravado como imagem.
        /// </summary>
        [CommandMethod("FIBRA_IMPORTAR_RUAS")]
        public void Import()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            // Geolocalização gravada por versões anteriores do plugin, com a transformação errada (o mapa de fundo ia para
            // outro lugar e o AutoCAD podia travar baixando ele): já sai consertada antes de tudo
            if (UtmZone.Get(db) is UtmSettings zoneNow && UtmZone.HasGeoLocation(db))
                UtmZone.CheckGeoLocation(db, zoneNow, out _);

            // Ruas que já estão no desenho (para a janela oferecer escrever só os nomes)
            UserSettings prefs = UserSettings.Current;
            string layer = prefs.RoadLayer;
            List<DrawnRoad> drawn;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                drawn = DrawnRoads(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead), layer);
            }

            // Tudo numa janela; os valores da última vez neste desenho voltam preenchidos
            UtmSettings? projectZone = UtmZone.Get(db);
            RoadImportSettings settings;
            RoadArea area;
            using (var form = new UI.RoadsForm(RoadImportSettings.Load(db), projectZone, drawn, layer))
            {
                form.PickInDrawing = rectangle => Pick(ed, form, rectangle);
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK || form.Area == null) return;
                settings = form.Settings;
                area = form.Area;
            }

            UtmSettings utm = area.Utm;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                settings.Save(tr, db);
                if (projectZone == null) UtmZone.Set(tr, db, utm); // A zona usada passa a ser a do projeto
                tr.Commit();
            }
            if (projectZone == null)
                ed.WriteMessage($"\n[INFO]: Zona UTM do projeto definida: {utm.Zone} {(utm.South ? "Sul" : "Norte")} (troca em Configurações > Projeto).");

            double minX = area.MinX, minY = area.MinY, maxX = area.MaxX, maxY = area.MaxY;
            // Só os nomes: as ruas já estão no desenho; o nome vai apoiado no eixo se houver eixo do plugin (linha aberta
            // na layer das ruas) ali
            bool namesOnly = settings.NamesOnly;
            bool outline = !namesOnly && settings.Style != "Eixo", axes = !namesOnly && settings.Style != "Contorno";
            bool includeNames = namesOnly || settings.IncludeNames;
            bool besideAxis = namesOnly
                ? drawn.Any(r => !r.Closed && r.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase) && r.Touches(area))
                : axes;
            bool onlyVehicles = !settings.IncludePaths;

            // Retângulo geográfico que cobre a área (os cantos UTM não ficam alinhados com lat/lon)
            var corners = new[] { (minX, minY), (minX, maxY), (maxX, minY), (maxX, maxY) }
                .Select(c => UtmZone.ToGeographic(c.Item1, c.Item2, utm.Zone, utm.South)).ToList();

            // O download roda em segundo plano: o AutoCAD continua respondendo e o Esc cancela
            ed.WriteMessage("\nConsultando o OpenStreetMap (pode levar até um minuto; Esc cancela)...");
            List<OsmRoad> roads;
            var cancel = new CancellationTokenSource();
            Task<List<OsmRoad>> download = Task.Run(() => OsmRoads.Download(corners.Min(c => c.Lat), corners.Min(c => c.Lon),
                corners.Max(c => c.Lat), corners.Max(c => c.Lon), onlyVehicles, cancel.Token));
            // Quando o download terminar (inclusive depois de cancelado), descarta o cancelamento e o erro dele
            download.ContinueWith(t => { _ = t.Exception; cancel.Dispose(); }, TaskScheduler.Default);
            // Espera pelo WaitHandle: o Task.Wait jogaria o erro do download aqui, fora do tratamento lá embaixo
            while (!((IAsyncResult)download).AsyncWaitHandle.WaitOne(100))
            {
                System.Windows.Forms.Application.DoEvents();
                if (!HostApplicationServices.Current.UserBreak()) continue;
                // Devolve o AutoCAD na hora: a conexão é abortada e o download termina sozinho em segundo plano
                try { cancel.Cancel(); } catch (ObjectDisposedException) { } // Terminou no mesmo instante do Esc
                ed.WriteMessage("\n[INFO]: Importar Ruas cancelado.");
                return;
            }
            try
            {
                roads = download.Result;
            }
            catch (AggregateException ex)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível consultar o OpenStreetMap ({ex.InnerException?.Message ?? ex.Message}). " +
                                "Confira a internet e tente de novo.");
                return;
            }

            var projected = roads
                .Select(r => (Road: r, Axis: r.Coords.Select(c => UtmZone.FromGeographic(c.Lat, c.Lon, utm.Zone, utm.South)).ToList()))
                .ToList();

            // Uma layer só para as ruas, com a aparência escolhida em Configurações (criada só se vier alguma rua)
            var counts = new Dictionary<string, int>();
            int names = 0, replaced = 0, lockedNames = 0;
            string? layerWarning = null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                bool layerReady = false;
                void EnsureLayer()
                {
                    if (layerReady) return;
                    layerWarning = CadHelpers.EnsureLayer(tr, db, layer, prefs.RoadStyle);
                    layerReady = true;
                }

                void Draw(List<(double X, double Y)> vertices, string kind, bool closed)
                {
                    EnsureLayer();
                    var poly = new Polyline { Layer = layer, Closed = closed };
                    for (int i = 0; i < vertices.Count; i++) poly.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
                    CadHelpers.Append(tr, modelSpace, poly);
                    counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
                }

                if (outline)
                {
                    // Leito unido: ruas e calçadas/ciclovias/trilhas unidas separadamente, para não virarem uma coisa só.
                    // Os eixos passam um pouco da borda para a rua chegar inteira até o corte.
                    foreach (bool vehicles in new[] { true, false })
                    {
                        var strips = projected
                            .Where(p => OsmRoads.IsVehicle(p.Road.Highway) == vehicles)
                            .SelectMany(p => OsmRoads.Clip(p.Axis, minX - Margin, minY - Margin, maxX + Margin, maxY + Margin)
                                .Select(run => ((IList<(double X, double Y)>)run, p.Road.WidthM)))
                            .ToList();
                        if (strips.Count == 0) continue;

                        foreach (List<(double X, double Y)> ring in OsmRoads.Pavement(strips, minX, minY, maxX, maxY))
                        {
                            Draw(ring, vehicles ? "contorno(s) de rua" : "contorno(s) de calçada/caminho", closed: true);
                        }
                    }
                }

                if (axes)
                {
                    foreach (var (_, axis) in projected)
                    {
                        foreach (List<(double X, double Y)> run in OsmRoads.Clip(axis, minX, minY, maxX, maxY))
                        {
                            Draw(run, "eixo(s)", closed: false);
                        }
                    }
                }

                if (includeNames)
                {
                    double height = DrawingScale.TextHeight(db) * NameScale;
                    var named = projected
                        .Where(p => p.Road.Label.Length > 0)
                        .SelectMany(p => OsmRoads.Clip(p.Axis, minX, minY, maxX, maxY).Select(run => (p.Road.Label, (IList<(double X, double Y)>)run)))
                        .ToList();
                    List<StreetLabel> labels = StreetLabels.Place(named, height, NameSpacing * DrawingScale.Factor(db));
                    if (labels.Count > 0) EnsureLayer();

                    // Só os nomes, de novo na mesma área: os nomes de rua que já estavam ali (na layer das ruas) e que vão
                    // ser escritos agora saem, para não ficarem dois. Os de rua que não ganha nome agora (trecho curto
                    // demais nesta área) ficam, e nome escrito à mão diferente do OSM também.
                    if (namesOnly && labels.Count > 0)
                    {
                        var placed = new HashSet<string>(labels.Select(l => l.Text.Trim()), StringComparer.OrdinalIgnoreCase);
                        List<ObjectId> old = modelSpace.Cast<ObjectId>()
                            .Where(id => tr.GetObject(id, OpenMode.ForRead) is MText t &&
                                         t.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase) &&
                                         t.Location.X >= minX && t.Location.X <= maxX && t.Location.Y >= minY && t.Location.Y <= maxY &&
                                         placed.Contains(t.Text.Trim()))
                            .ToList();
                        lockedNames = CadHelpers.EraseUnlocked(tr, old);
                        replaced = old.Count - lockedNames;
                    }

                    foreach (StreetLabel label in labels)
                    {
                        // Só com o contorno, o nome fica no meio da rua; com o eixo desenhado, apoiado logo acima dele
                        // (entre o eixo e o meio-fio, sem encostar na linha)
                        var at = new Point3d(label.X, label.Y, 0);
                        AttachmentPoint attachment = AttachmentPoint.MiddleCenter;
                        if (besideAxis)
                        {
                            at += new Vector3d(-Math.Sin(label.Angle), Math.Cos(label.Angle), 0) * height * 0.25;
                            attachment = AttachmentPoint.BottomCenter;
                        }
                        MText text = CadHelpers.AddText(tr, modelSpace, at, CadHelpers.MTextLiteral(label.Text), label.Angle, attachment, layer);
                        text.TextHeight = height;
                        names++;
                    }
                }
                tr.Commit();
            }

            // Sem ruas (ou sem nomes) na área, o mapa de fundo pedido sai assim mesmo
            bool nothing = namesOnly ? names == 0 : counts.Count == 0;
            bool map = settings.Background != "Nenhum";
            if (namesOnly)
            {
                if (names == 0)
                {
                    ed.WriteMessage("\n[AVISO]: Nenhuma rua com nome nessa área no OpenStreetMap (ou os trechos são curtos demais para o texto).");
                    if (!map) return;
                }
                else
                {
                    ed.WriteMessage($"\n[SUCESSO]: {names} nome(s) de rua na layer {layer}, sem desenhar as ruas de novo" +
                                    (replaced > 0 ? $" ({replaced} nome(s) que já estavam ali foram trocados)." : ".") +
                                    " Os nomes seguem o eixo das ruas do OpenStreetMap.");
                }
                if (lockedNames > 0)
                    ed.WriteMessage($"\n[AVISO]: {lockedNames} nome(s) antigo(s) em layer travada ficaram: pode haver nomes repetidos.");
            }
            else if (nothing)
            {
                ed.WriteMessage("\n[AVISO]: Nenhuma rua encontrada nessa área.");
                if (!map) return;
            }
            else
            {
                ed.WriteMessage($"\n[SUCESSO]: {roads.Count} via(s) do OpenStreetMap na layer {layer}: " +
                                string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Value} {c.Key}")) +
                                (includeNames ? $"; {names} nome(s) de rua" : "") + ".");
                if (includeNames && names == 0)
                    ed.WriteMessage("\n[AVISO]: Nenhuma rua com nome nessa área no OpenStreetMap (ou os trechos são curtos demais para o texto).");
            }
            if (layerWarning != null) ed.WriteMessage($"\n[AVISO]: {layerWarning}");

            // Georreferenciado por padrão: as coordenadas já são UTM; grava também a geolocalização do AutoCAD
            var reference = new Point3d((minX + maxX) / 2, (minY + maxY) / 2, 0);
            if (UtmZone.Georeference(db, utm, reference) is string code)
            {
                ed.WriteMessage($"\n[INFO]: Desenho georreferenciado: {code}, em metros (GEOGRAPHICLOCATION para conferir).");
            }
            else if (UtmZone.HasGeoLocation(db))
            {
                if (UtmZone.FromGeoLocation(db) is UtmSettings geo && (geo.Zone != utm.Zone || geo.South != utm.South))
                    ed.WriteMessage($"\n[AVISO]: A geolocalização do desenho é da zona {geo.Zone}{(geo.South ? "S" : "N")}, " +
                                    $"mas a Zona UTM do projeto é {utm.Zone}{(utm.South ? "S" : "N")}: confira.");
            }
            else
            {
                ed.WriteMessage($"\n[AVISO]: O AutoCAD não aceitou gravar a geolocalização; as ruas estão em UTM {utm.Zone}{(utm.South ? "S" : "N")} mesmo assim.");
            }
            if (!nothing) ed.WriteMessage("\n[INFO]: Dados © colaboradores do OpenStreetMap (ODbL)." + (namesOnly ? "" : " Largura das ruas aproximada."));

            doc.SendStringToExecute(FormattableString.Invariant($"_.ZOOM _W *{minX:F3},{minY:F3} *{maxX:F3},{maxY:F3} "), true, false, false);

            // Mapa de fundo do AutoCAD, depois do zoom (precisa da geolocalização gravada acima, e certa: errada, o mapa vai
            // para outro lugar e o AutoCAD pode travar baixando ele)
            if (map) ShowMap(doc, settings, utm, reference, minX, minY, maxX, maxY);
        }

        private static void ShowMap(Document doc, RoadImportSettings settings, UtmSettings utm, Point3d center,
            double minX, double minY, double maxX, double maxY)
        {
            Editor ed = doc.Editor;
            if (!UtmZone.HasGeoLocation(doc.Database))
            {
                ed.WriteMessage("\n[AVISO]: Sem a geolocalização do desenho o AutoCAD não mostra o mapa de fundo.");
                return;
            }
            switch (UtmZone.CheckGeoLocation(doc.Database, utm, center, out string detail))
            {
                case UtmZone.GeoCheck.Wrong:
                    ed.WriteMessage($"\n[AVISO]: A geolocalização do desenho põe o centro da área em {detail}: o mapa iria para " +
                                    "outro lugar, então não foi ligado. Confira o sistema de coordenadas em GEOGRAPHICLOCATION.");
                    return;
                case UtmZone.GeoCheck.Unknown:
                    ed.WriteMessage("\n[AVISO]: Não deu para conferir a geolocalização do desenho; o mapa pode não aparecer no lugar certo.");
                    break;
                default:
                    var (lat, lon) = UtmZone.ToGeographic(center.X, center.Y, utm.Zone, utm.South);
                    ed.WriteMessage(FormattableString.Invariant($"\n[INFO]: Geolocalização conferida: centro da área em lat {lat:F6}, long {lon:F6}."));
                    break;
            }
            RoadsMapCommand.Show(doc, settings.Background, settings.BackgroundImage, minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Linhas de rua que já estão no desenho: na layer das ruas ou numa layer com nome de rua (desenhadas à mão), soltas
        /// no Model ou dentro de um XREF/bloco inserido nele (a base da prefeitura costuma vir assim: BASE|MEIO-FIO).
        /// </summary>
        private static List<DrawnRoad> DrawnRoads(Transaction tr, BlockTableRecord modelSpace, string layer)
        {
            var drawn = new List<DrawnRoad>();
            var inBlocks = new Dictionary<ObjectId, List<(string Layer, bool Closed, Extents3d Extents)>>(); // Por definição de bloco
            foreach (ObjectId id in modelSpace)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Curve curve)
                {
                    if (DrawnRoad.IsRoadLayer(curve.Layer, layer) && Extents(curve) is Extents3d e) drawn.Add(Road(curve.Layer, curve.Closed, e));
                }
                else if (obj is BlockReference br)
                {
                    if (!inBlocks.TryGetValue(br.BlockTableRecord, out var curves))
                    {
                        curves = RoadCurvesIn(tr, br.BlockTableRecord, layer);
                        inBlocks[br.BlockTableRecord] = curves;
                    }
                    foreach (var (name, closed, local) in curves) drawn.Add(Road(name, closed, Transform(local, br.BlockTransform)));
                }
            }
            return drawn;

            static DrawnRoad Road(string name, bool closed, Extents3d e) => new DrawnRoad
            {
                Layer = name,
                MinX = e.MinPoint.X, MinY = e.MinPoint.Y, MaxX = e.MaxPoint.X, MaxY = e.MaxPoint.Y,
                Closed = closed
            };
        }

        /// <summary>Linhas de rua soltas dentro da definição do bloco/XREF: layer, se é fechada e extensão nas coordenadas dele.</summary>
        private static List<(string Layer, bool Closed, Extents3d Extents)> RoadCurvesIn(Transaction tr, ObjectId blockId, string layer)
        {
            var curves = new List<(string, bool, Extents3d)>();
            var definition = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            if (definition.IsLayout) return curves;
            foreach (ObjectId id in definition)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Curve curve && DrawnRoad.IsRoadLayer(curve.Layer, layer) &&
                    Extents(curve) is Extents3d e)
                {
                    curves.Add((curve.Layer, curve.Closed, e));
                }
            }
            return curves;
        }

        private static Extents3d? Extents(Entity ent)
        {
            try
            {
                return ent.GeometricExtents;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null; // Linha sem extensão (vazia)
            }
        }

        /// <summary>Retângulo (no mundo) que cobre a extensão local transformada pelo bloco.</summary>
        private static Extents3d Transform(Extents3d local, Matrix3d transform)
        {
            Point3d a = local.MinPoint, b = local.MaxPoint;
            Point3d[] corners = new[] { a, new Point3d(b.X, a.Y, 0), b, new Point3d(a.X, b.Y, 0) }.Select(c => c.TransformBy(transform)).ToArray();
            var box = new Extents3d(corners[0], corners[0]);
            foreach (Point3d corner in corners.Skip(1)) box.AddPoint(corner);
            return box;
        }

        /// <summary>
        /// Botão "Marcar no desenho" da janela: esconde a janela enquanto o usuário clica os dois cantos (ou o centro)
        /// e devolve os pontos em coordenadas do mundo (as UTM do desenho). Null se cancelar.
        /// </summary>
        private static (double X1, double Y1, double X2, double Y2)? Pick(Editor ed, System.Windows.Forms.Form form, bool rectangle)
        {
            using (EditorUserInteraction interaction = ed.StartUserInteraction(form))
            {
                PromptPointResult first = ed.GetPoint(rectangle ? "\nPrimeiro canto da área: " : "\nCentro da área: ");
                if (first.Status != PromptStatus.OK) return null;

                Matrix3d ucs = ed.CurrentUserCoordinateSystem;
                Point3d a = first.Value.TransformBy(ucs), b = a;
                if (rectangle)
                {
                    PromptPointResult second = ed.GetCorner("\nCanto oposto: ", first.Value);
                    if (second.Status != PromptStatus.OK) return null;
                    b = second.Value.TransformBy(ucs);
                }
                interaction.End();
                form.Focus();
                return (a.X, a.Y, b.X, b.Y);
            }
        }
    }
}
