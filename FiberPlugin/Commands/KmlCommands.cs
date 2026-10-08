using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>Troca de dados com o Google Earth (KML/KMZ), convertendo latitude/longitude para a zona UTM do projeto.</summary>
    public class KmlCommands
    {
        private const string LayerPrefix = "KML-";
        private const double IconSize = 3.0;      // m, na escala 1:1000 (vezes a escala do ícone no KML)

        /// <summary>Cor de cada layer de ícone, para os ícones sem cor no KML.</summary>
        private static readonly Dictionary<string, short> ShapeColors = new Dictionary<string, short>
        {
            [IconShapes.Triangle] = 2, [IconShapes.Circle] = 4, [IconShapes.Square] = 5, [IconShapes.Diamond] = 6,
            [IconShapes.Hexagon] = 30, [IconShapes.Star] = 40, [IconShapes.Target] = 1, [IconShapes.Marker] = 1,
            [IconShapes.House] = 50, [IconShapes.Point] = 7
        };

        // ---------- Importar ----------

        /// <summary>
        /// Traz pontos, linhas e polígonos de um KML/KMZ para o desenho, na zona UTM do projeto. Pontos viram postes
        /// numerados (modelo escolhido na lista) ou o mesmo ícone do Google Earth (triângulo, círculo, quadrado...),
        /// com a cor do KML e uma layer por forma; linhas viram cabos (com nome e metragem vão a vão) ou polilinhas;
        /// polígonos viram polilinhas fechadas. Linhas e polígonos levam a cor do KML.
        /// </summary>
        [CommandMethod("FIBRA_IMPORTAR_KML")]
        public void Import()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string? path = CadHelpers.AskOpenPath("Importar KML", "Google Earth (*.kml;*.kmz)|*.kml;*.kmz");
            if (path == null) return;

            List<KmlFeature> features;
            try
            {
                features = Kml.Read(path);
            }
            catch (System.Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Xml.XmlException)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível ler {Path.GetFileName(path)} ({ex.Message}).");
                return;
            }
            if (features.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: O arquivo não tem pontos, linhas nem polígonos.");
                return;
            }

            List<KmlFeature> points = features.Where(f => f.Kind == KmlKind.Point).ToList();
            List<KmlFeature> lines = features.Where(f => f.Kind == KmlKind.Line).ToList();
            List<KmlFeature> polygons = features.Where(f => f.Kind == KmlKind.Polygon).ToList();
            ed.WriteMessage($"\n[INFO]: {Path.GetFileName(path)}: {points.Count} ponto(s), {lines.Count} linha(s), {polygons.Count} polígono(s).");

            // Zona do desenho; sem ela, a do primeiro ponto do KML (gravada só na hora de desenhar, não se cancelar antes)
            UtmSettings? projectZone = UtmZone.Get(db);
            (double Lon, double Lat) first = features[0].Coords[0];
            UtmSettings utm = projectZone ?? new UtmSettings { Zone = UtmZone.ZoneFor(first.Lon), South = first.Lat < 0 };
            WarnOutOfZone(ed, features, utm);

            // O que fazer com cada tipo (perguntado antes de desenhar)
            string pointMode = points.Count == 0 ? "Ignorar"
                : CadHelpers.AskKeyword(ed, $"\nPontos do KML ({points.Count}) como [Postes/Icones/Ignorar] <Icones>: ", "Postes Icones Ignorar", "Icones") ?? "";
            if (pointMode.Length == 0) return;
            string lineMode = lines.Count == 0 ? "Ignorar"
                : CadHelpers.AskKeyword(ed, $"\nLinhas do KML ({lines.Count}) como [Cabos/Linhas/Ignorar] <Linhas>: ", "Cabos Linhas Ignorar", "Linhas") ?? "";
            if (lineMode.Length == 0) return;

            // Postes: modelo, bloco e primeiro número
            PoleData? model = null;
            ObjectId poleBlock = ObjectId.Null;
            HashSet<int> used = new HashSet<int>();
            int number = 1;
            if (pointMode == "Postes")
            {
                List<PoleData> models = PoleModels.Load(ed);
                if (models.Count == 0) return;
                model = InsertPoleCommand.ChooseModel(models);
                if (model == null) return;
                poleBlock = InsertPoleCommand.LoadBlock(ed, db, model.Type);
                if (poleBlock.IsNull) return;

                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    used = Poles.UsedNumbers(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                }
                int? start = Poles.AskNumber(ed, Poles.NextFree(used, 1), "do primeiro poste");
                if (start == null) return;
                number = start.Value;
            }

            CableModel? cable = null;
            if (lineMode == "Cabos")
            {
                cable = CadHelpers.SelectCable(ed, "Importar");
                if (cable == null) return;
            }

            Point3d ToDrawing((double Lon, double Lat) c)
            {
                var (e, n) = UtmZone.FromGeographic(c.Lat, c.Lon, utm.Zone, utm.South);
                return new Point3d(e, n, 0);
            }

            int poles = 0, cables = 0, polylines = 0;
            var icons = new Dictionary<string, int>();
            double scale = DrawingScale.Factor(db);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                if (projectZone == null)
                {
                    UtmZone.Set(tr, db, utm);
                    ed.WriteMessage($"\n[INFO]: Zona UTM do projeto definida pelo KML: {utm.Zone} {(utm.South ? "Sul" : "Norte")} (troca em Configurações > Projeto).");
                }

                foreach (KmlFeature f in points)
                {
                    Point3d point = ToDrawing(f.Coords[0]);
                    if (pointMode == "Postes")
                    {
                        number = Poles.NextFree(used, number);
                        var data = new PoleData { Number = number, Type = model!.Type, HeightM = model.HeightM, EffortDaN = model.EffortDaN };
                        InsertPoleCommand.PlacePole(tr, db, modelSpace, poleBlock, point, 0, data, utm);
                        used.Add(number);
                        poles++;
                    }
                    else if (pointMode == "Icones")
                    {
                        // Mesma forma do ícone do Google Earth, numa layer só por forma (KML-TRIANGULO, KML-CIRCULO...)
                        string shape = IconShapes.Classify(f.IconHref, path);
                        double size = IconSize * scale * Math.Max(0.5, Math.Min(2.0, f.IconScale));
                        var icon = new BlockReference(point, KmlSymbols.Ensure(tr, db, shape))
                        {
                            Layer = Layer(tr, db, shape, ShapeColors[shape]),
                            ScaleFactors = new Scale3d(size)
                        };
                        if (KmlColor(f.IconColor) is Autodesk.AutoCAD.Colors.Color color) icon.Color = color;
                        CadHelpers.Append(tr, modelSpace, icon);
                        icons[shape] = icons.TryGetValue(shape, out int n) ? n + 1 : 1;
                    }
                }

                foreach (KmlFeature f in lines.Where(_ => lineMode != "Ignorar").Concat(polygons))
                {
                    List<Point3d> vertices = f.Coords.Select(ToDrawing).ToList();
                    if (f.Kind == KmlKind.Line && lineMode == "Cabos")
                    {
                        CableDrawing.Draw(tr, db, modelSpace, vertices, CableDrawing.SpanLengths(vertices), cable!);
                        cables++;
                        continue;
                    }

                    // Polígono: o KML repete o primeiro ponto no fim; a polilinha fechada não precisa
                    if (f.Kind == KmlKind.Polygon && vertices.Count > 2 && vertices[0].DistanceTo(vertices[vertices.Count - 1]) < 0.01)
                        vertices.RemoveAt(vertices.Count - 1);

                    var poly = new Polyline
                    {
                        Layer = f.Kind == KmlKind.Polygon ? Layer(tr, db, "POLIGONOS", 6) : Layer(tr, db, "LINHAS", 30),
                        Closed = f.Kind == KmlKind.Polygon
                    };
                    if (KmlColor(f.LineColor) is Autodesk.AutoCAD.Colors.Color lineColor) poly.Color = lineColor;
                    for (int i = 0; i < vertices.Count; i++) poly.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
                    CadHelpers.Append(tr, modelSpace, poly);
                    polylines++;
                }

                tr.Commit();
            }

            ed.WriteMessage("\n[SUCESSO]: Importado: " + string.Join(", ", new[]
            {
                poles > 0 ? $"{poles} poste(s)" : "",
                icons.Count > 0 ? $"{icons.Values.Sum()} ícone(s) (" + string.Join(", ", icons.OrderByDescending(i => i.Value).Select(i => $"{i.Value} {i.Key.ToLowerInvariant()}")) + ")" : "",
                cables > 0 ? $"{cables} cabo(s)" : "",
                polylines > 0 ? $"{polylines} polilinha(s)" : ""
            }.Where(s => s.Length > 0)) + ".");
            if (poles > 0) ed.WriteMessage("\n[DICA]: Postes DT entram com rotação 0°; use o comando GIRAR do AutoCAD se precisar alinhar.");

            doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
        }

        /// <summary>Pontos a mais de 1° além da borda da zona saem distorcidos.</summary>
        private static void WarnOutOfZone(Editor ed, List<KmlFeature> features, UtmSettings utm)
        {
            double center = utm.Zone * 6 - 183;
            int outside = features.SelectMany(f => f.Coords).Count(c => Math.Abs(c.Lon - center) > 4);
            if (outside > 0)
            {
                ed.WriteMessage($"\n[AVISO]: {outside} coordenada(s) bem fora da zona {utm.Zone}: confira se o KML é desta região.");
            }
        }

        /// <summary>Layer do tipo importado: KML-TRIANGULO, KML-LINHAS, KML-POLIGONOS...</summary>
        private static string Layer(Transaction tr, Database db, string kind, short color)
        {
            string name = LayerPrefix + kind;
            CadHelpers.EnsureLayer(tr, db, name, color);
            return name;
        }

        /// <summary>Cor do KML (aabbggrr) em cor verdadeira do AutoCAD; null se não houver ou for inválida.</summary>
        private static Autodesk.AutoCAD.Colors.Color? KmlColor(string? kml)
        {
            if (kml == null || kml.Length != 8 || !uint.TryParse(kml, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return null;
            byte r = (byte)(v & 0xFF), g = (byte)((v >> 8) & 0xFF), b = (byte)((v >> 16) & 0xFF);
            return Autodesk.AutoCAD.Colors.Color.FromRgb(r, g, b);
        }

        // ---------- Exportar ----------

        /// <summary>
        /// Gera um KMZ/KML do projeto para o Google Earth: postes (com modelo, ID Energisa e esforço), CTO e CEO,
        /// equipamentos da Energisa e cabos (uma cor por tipo, com metragem), em pastas separadas.
        /// </summary>
        [CommandMethod("FIBRA_EXPORTAR_KML")]
        public void Export()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            UtmSettings? utm = UtmZone.Get(db) ?? UtmZone.Ask(ed, db, null);
            if (utm == null) return;

            List<CableModel> catalog = CableProvider.GetCables();
            ProjectData project = ProjectData.Collect(db, catalog);
            var cableLines = new List<(string Name, List<Point3d> Vertices, double Length)>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Polyline poly || XDataTags.GetCableName(poly) is not string name) continue;
                    var vertices = Enumerable.Range(0, poly.NumberOfVertices).Select(poly.GetPoint3dAt).ToList();
                    cableLines.Add((name, vertices, poly.Length));
                }
            }
            if (project.PoleList.Count == 0 && project.BoxList.Count == 0 && cableLines.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nada para exportar: o desenho não tem postes, CTO/CEO nem cabos do plugin.");
                return;
            }

            string title = Path.GetFileNameWithoutExtension(doc.Name);
            string? path = CadHelpers.AskSavePath("Exportar KML", title + ".kmz", "Google Earth (*.kmz)|*.kmz|KML (*.kml)|*.kml");
            if (path == null) return;

            (double Lon, double Lat) Geo(Point3d p)
            {
                var (lat, lon) = UtmZone.ToGeographic(p.X, p.Y, utm.Zone, utm.South);
                return (lon, lat);
            }
            string Utm(Point3d p) => $"{UtmZone.ZoneText(p, utm)}  {UtmZone.EastingText(p.X)}  {UtmZone.NorthingText(p.Y, utm.South)}";

            var kml = new Kml.Writer(title);
            const string Icons = "http://maps.google.com/mapfiles/kml/shapes/";
            kml.IconStyle("poste", Icons + "placemark_circle.png", Kml.Writer.Color(255, 255, 255), 0.9);
            kml.IconStyle("poste_excedido", Icons + "placemark_circle.png", Kml.Writer.Color(230, 40, 40), 1.1);
            kml.IconStyle("cto", Icons + "placemark_square.png", Kml.Writer.Color(18, 160, 75), 1.0);
            kml.IconStyle("ceo", Icons + "placemark_square.png", Kml.Writer.Color(27, 111, 168), 1.0);
            kml.IconStyle("equipamento", Icons + "triangle.png", Kml.Writer.Color(245, 194, 66), 0.9);

            // Postes, com o esforço do cálculo quando houver
            var efforts = project.Efforts.Where(e => e.Pole != null).GroupBy(e => e.Pole!.Id).ToDictionary(g => g.Key, g => g.First());
            if (project.PoleList.Count > 0)
            {
                var folder = kml.Folder("Postes");
                foreach (PoleInfo pole in project.PoleList)
                {
                    efforts.TryGetValue(pole.Id, out EffortPoint? effort);
                    string description = Kml.Table(
                        ("Modelo", pole.Data?.Designation ?? pole.Name),
                        ("Tipo", pole.Data?.TypeName ?? ""),
                        ("ID Energisa", pole.Data?.EnergisaId ?? ""),
                        ("Situação", effort?.Situation ?? ""),
                        ("Esforço total", effort != null ? $"{effort.Load.TotalKgf:F2} kgf" : ""),
                        ("Nominal", pole.NominalKgf is double nominal ? $"{nominal:F0} kgf" : ""),
                        ("Resultado", effort != null ? $"{effort.Load.Result} ({effort.Load.Usage:F0}%)" : ""),
                        ("UTM", Utm(pole.Position)));
                    var (lon, lat) = Geo(pole.Position);
                    kml.Point(folder, pole.Number, description, effort?.Load.Exceeded == true ? "poste_excedido" : "poste", lon, lat);
                }
            }

            if (project.BoxList.Count > 0)
            {
                var folder = kml.Folder("CTO e CEO");
                var polesByHandle = Poles.ByHandle(project.PoleList);
                foreach (BoxInfo box in project.BoxList)
                {
                    PoleInfo? pole = Boxes.PoleOf(box, polesByHandle, project.PoleList);
                    string description = Kml.Table(("Bloco", box.BlockName), ("Poste", pole?.Number ?? "Sem poste"), ("UTM", Utm(box.Position)));
                    var (lon, lat) = Geo(box.Position);
                    kml.Point(folder, box.Data.Id, description, box.Data.Kind == BlockCategories.Cto ? "cto" : "ceo", lon, lat);
                }
            }

            if (project.Equipment.Count > 0)
            {
                var folder = kml.Folder("Equipamentos Energisa");
                foreach (var (name, position) in project.Equipment)
                {
                    var (lon, lat) = Geo(position);
                    kml.Point(folder, name, Kml.Table(("UTM", Utm(position))), "equipamento", lon, lat);
                }
            }

            if (cableLines.Count > 0)
            {
                var palette = new[] { (18, 160, 75), (240, 120, 30), (27, 160, 220), (200, 60, 170), (245, 200, 40), (230, 50, 50) };
                var styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in cableLines.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var (r, g, b) = palette[styles.Count % palette.Length];
                    string id = "cabo" + styles.Count;
                    kml.LineStyle(id, Kml.Writer.Color(r, g, b), 3);
                    styles[name] = id;
                }

                var folder = kml.Folder("Cabos");
                foreach (var (name, vertices, length) in cableLines)
                {
                    CableModel? model = CableProvider.Find(catalog, name);
                    string description = Kml.Table(("Cabo", model?.FullName ?? name), ("Nome curto", name), ("Metragem", $"{length:F1} m"),
                        ("Vãos", (vertices.Count - 1).ToString()));
                    kml.Line(folder, name, description, styles[name], vertices.Select(Geo));
                }
            }

            try
            {
                kml.Save(path);
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível gravar {Path.GetFileName(path)} ({ex.Message}).");
                return;
            }

            ed.WriteMessage($"\n[SUCESSO]: {path}: {project.PoleList.Count} poste(s), {project.BoxList.Count} CTO/CEO, " +
                            $"{project.Equipment.Count} equipamento(s), {cableLines.Count} cabo(s).");
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.Exception)
            {
                // Sem Google Earth instalado: o arquivo continua salvo
            }
        }
    }
}
