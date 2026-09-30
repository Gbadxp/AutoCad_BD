using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Blocos dos ícones do Google Earth (KML_TRIANGULO, KML_CIRCULO...), criados no desenho na primeira importação.
    /// Têm 1 unidade de tamanho, centro no ponto (o marcador tem a ponta no ponto) e cor PorBloco, então cada ponto
    /// importado leva a cor do ícone do KML.
    /// </summary>
    public static class KmlSymbols
    {
        public const string BlockPrefix = "KML_";
        private const double Width = 0.1;   // Espessura do contorno, relativa ao tamanho do símbolo

        public static ObjectId Ensure(Transaction tr, Database db, string shape)
        {
            string name = BlockPrefix + shape;
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (table.Has(name)) return table[name];

            table.UpgradeOpen();
            var block = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            ObjectId id = table.Add(block);
            tr.AddNewlyCreatedDBObject(block, true);

            foreach (Entity ent in Geometry(shape))
            {
                ent.Layer = "0";
                ent.ColorIndex = 0; // PorBloco
                block.AppendEntity(ent);
                tr.AddNewlyCreatedDBObject(ent, true);
            }
            return id;
        }

        private static IEnumerable<Entity> Geometry(string shape)
        {
            switch (shape)
            {
                case IconShapes.Triangle:
                    return new[] { Closed((-0.5, -0.43), (0.5, -0.43), (0, 0.43)) };
                case IconShapes.Square:
                    return new[] { Closed((-0.45, -0.45), (0.45, -0.45), (0.45, 0.45), (-0.45, 0.45)) };
                case IconShapes.Diamond:
                    return new[] { Closed((0, 0.5), (0.5, 0), (0, -0.5), (-0.5, 0)) };
                case IconShapes.Hexagon:
                    return new[] { Closed(Enumerable.Range(0, 6).Select(i => Polar(0.5, i * 60)).ToArray()) };
                case IconShapes.Star:
                    return new[] { Closed(Enumerable.Range(0, 10).Select(i => Polar(i % 2 == 0 ? 0.5 : 0.2, 90 + i * 36)).ToArray()) };
                case IconShapes.Target:
                    return new[] { Ring(0, 0, 0.45), Ring(0, 0, 0.18) };
                case IconShapes.House:
                    return new[] { Closed((-0.42, -0.45), (0.42, -0.45), (0.42, 0.08), (0, 0.48), (-0.42, 0.08)) };
                case IconShapes.Marker:
                    // Gota com a ponta no ponto do KML e a cabeça em cima
                    return new Entity[]
                    {
                        Ring(0, 0.68, 0.28),
                        Open((-0.25, 0.56), (0, 0), (0.25, 0.56))
                    };
                case IconShapes.Point:
                    return new Entity[]
                    {
                        Ring(0, 0, 0.3),
                        new Line(new Point3d(-0.5, 0, 0), new Point3d(0.5, 0, 0)),
                        new Line(new Point3d(0, -0.5, 0), new Point3d(0, 0.5, 0))
                    };
                default: // Círculo
                    return new[] { Ring(0, 0, 0.45) };
            }
        }

        private static (double, double) Polar(double r, double deg) =>
            (r * Math.Cos(deg * Math.PI / 180), r * Math.Sin(deg * Math.PI / 180));

        private static Polyline Closed(params (double X, double Y)[] points)
        {
            Polyline poly = Open(points);
            poly.Closed = true;
            return poly;
        }

        private static Polyline Open(params (double X, double Y)[] points)
        {
            var poly = new Polyline();
            for (int i = 0; i < points.Length; i++) poly.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, Width, Width);
            return poly;
        }

        /// <summary>Círculo como polilinha de dois arcos (a polilinha tem espessura; o Circle não).</summary>
        private static Polyline Ring(double cx, double cy, double r)
        {
            var poly = new Polyline { Closed = true };
            poly.AddVertexAt(0, new Point2d(cx - r, cy), 1, Width, Width);
            poly.AddVertexAt(1, new Point2d(cx + r, cy), 1, Width, Width);
            return poly;
        }
    }
}
