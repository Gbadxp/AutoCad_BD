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
    /// <summary>Ruas do OpenStreetMap desenhadas na posição UTM do projeto.</summary>
    public class RoadsCommand
    {
        private const double Margin = 30;      // m além da borda: a rua chega inteira até o corte
        private const string OutlineLayer = "RUA_CONTORNO";
        private const string PathsLayer = "RUA_CAMINHOS";
        private const string NamesLayer = "RUA_NOMES";
        private const double NameScale = 1.25;   // Nomes com 2,5 mm no papel (o texto padrão do plugin tem 2 mm)
        private const double NameSpacing = 400;  // m entre repetições do nome numa rua longa, na escala 1:1000

        /// <summary>
        /// Abre a janela da área (retângulo pelos quatro lados ou centro e raio, em lat/long ou UTM, ou marcada no desenho),
        /// baixa as ruas do OpenStreetMap e desenha
        /// o contorno já unido (meio-fio, sem sobreposição nos cruzamentos), o eixo (uma layer por tipo de via) ou os dois.
        /// As ruas são cortadas na borda da área e ficam na zona UTM do projeto, alinhadas com postes e KML; o desenho
        /// recebe a geolocalização do AutoCAD se ainda não tiver.
        /// </summary>
        [CommandMethod("FIBRA_IMPORTAR_RUAS")]
        public void Import()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Tudo numa janela; os valores da última vez neste desenho voltam preenchidos
            UtmSettings? projectZone = UtmZone.Get(db);
            RoadImportSettings settings;
            RoadArea area;
            using (var form = new UI.RoadsForm(RoadImportSettings.Load(db), projectZone))
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
                ed.WriteMessage($"\n[INFO]: Zona UTM do projeto definida: {utm.Zone} {(utm.South ? "Sul" : "Norte")} (botão Zona UTM para trocar).");

            double minX = area.MinX, minY = area.MinY, maxX = area.MaxX, maxY = area.MaxY;
            bool outline = settings.Style != "Eixo", axes = settings.Style != "Contorno";
            bool onlyVehicles = !settings.IncludePaths;

            // Retângulo geográfico que cobre a área (os cantos UTM não ficam alinhados com lat/lon)
            var corners = new[] { (minX, minY), (minX, maxY), (maxX, minY), (maxX, maxY) }
                .Select(c => UtmZone.ToGeographic(c.Item1, c.Item2, utm.Zone, utm.South)).ToList();

            ed.WriteMessage("\nConsultando o OpenStreetMap (pode levar até um minuto)...");
            System.Windows.Forms.Application.DoEvents();
            List<OsmRoad> roads;
            try
            {
                roads = OsmRoads.Download(corners.Min(c => c.Lat), corners.Min(c => c.Lon),
                    corners.Max(c => c.Lat), corners.Max(c => c.Lon), onlyVehicles);
            }
            catch (IOException ex)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível consultar o OpenStreetMap ({ex.Message}). Confira a internet e tente de novo.");
                return;
            }

            var projected = roads
                .Select(r => (Road: r, Axis: r.Coords.Select(c => UtmZone.FromGeographic(c.Lat, c.Lon, utm.Zone, utm.South)).ToList()))
                .ToList();

            var counts = new Dictionary<string, int>();
            int names = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                void Draw(List<(double X, double Y)> vertices, string layer, short color, bool closed)
                {
                    if (!counts.ContainsKey(layer))
                    {
                        CadHelpers.EnsureLayer(tr, db, layer, color);
                        counts[layer] = 0;
                    }
                    var poly = new Polyline { Layer = layer, Closed = closed };
                    for (int i = 0; i < vertices.Count; i++) poly.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
                    CadHelpers.Append(tr, modelSpace, poly);
                    counts[layer]++;
                }

                if (outline)
                {
                    // Leito unido: ruas numa layer e calçadas/ciclovias/trilhas em outra, para não virarem uma coisa só.
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
                            Draw(ring, vehicles ? OutlineLayer : PathsLayer, vehicles ? (short)7 : (short)4, closed: true);
                        }
                    }
                }

                if (axes)
                {
                    foreach (var (road, axis) in projected)
                    {
                        foreach (List<(double X, double Y)> run in OsmRoads.Clip(axis, minX, minY, maxX, maxY))
                        {
                            Draw(run, OsmRoads.LayerFor(road.Highway), OsmRoads.ColorFor(road.Highway), closed: false);
                        }
                    }
                }

                if (settings.IncludeNames)
                {
                    double height = DrawingScale.TextHeight(db) * NameScale;
                    var named = projected
                        .Where(p => p.Road.Label.Length > 0)
                        .SelectMany(p => OsmRoads.Clip(p.Axis, minX, minY, maxX, maxY).Select(run => (p.Road.Label, (IList<(double X, double Y)>)run)));
                    List<StreetLabel> labels = StreetLabels.Place(named, height, NameSpacing * DrawingScale.Factor(db));
                    if (labels.Count > 0) CadHelpers.EnsureLayer(tr, db, NamesLayer, 7);

                    foreach (StreetLabel label in labels)
                    {
                        // Só com o contorno, o nome fica no meio da rua; com o eixo desenhado, apoiado logo acima dele
                        // (entre o eixo e o meio-fio, sem encostar na linha)
                        var at = new Point3d(label.X, label.Y, 0);
                        AttachmentPoint attachment = AttachmentPoint.MiddleCenter;
                        if (axes)
                        {
                            at += new Vector3d(-Math.Sin(label.Angle), Math.Cos(label.Angle), 0) * height * 0.25;
                            attachment = AttachmentPoint.BottomCenter;
                        }
                        MText text = CadHelpers.AddText(tr, modelSpace, at, MTextLiteral(label.Text), label.Angle, attachment, NamesLayer);
                        text.TextHeight = height;
                        names++;
                    }
                }
                tr.Commit();
            }

            if (counts.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhuma rua encontrada nessa área.");
                return;
            }
            ed.WriteMessage($"\n[SUCESSO]: {roads.Count} via(s) do OpenStreetMap: " +
                            string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Value} polilinha(s) em {c.Key}")) +
                            (settings.IncludeNames ? $"; {names} nome(s) de rua em {NamesLayer}" : "") + ".");
            if (settings.IncludeNames && names == 0)
                ed.WriteMessage("\n[AVISO]: Nenhuma rua com nome nessa área no OpenStreetMap (ou os trechos são curtos demais para o texto).");

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
            ed.WriteMessage("\n[INFO]: Dados © colaboradores do OpenStreetMap (ODbL). Largura das ruas aproximada.");

            doc.SendStringToExecute(FormattableString.Invariant($"_.ZOOM _W *{minX:F3},{minY:F3} *{maxX:F3},{maxY:F3} "), true, false, false);
        }

        /// <summary>Texto puro para o MText (barra invertida e chaves são códigos de formatação).</summary>
        private static string MTextLiteral(string text) => text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

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
