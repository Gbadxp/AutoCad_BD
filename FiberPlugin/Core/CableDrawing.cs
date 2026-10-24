using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>Metragem de um tipo de cabo no desenho (relatório e memorial descritivo).</summary>
    public class CableTotal
    {
        public string Name { get; set; } = "";       // Nome curto (ex.: ASU-80 06F.O)
        public CableModel? Model { get; set; }       // Null se o cabo não está cadastrado
        public int Runs { get; set; }                // Quantidade de lances (polilinhas)
        public double Length { get; set; }           // Metros

        /// <summary>Nome completo do cadastro ou, sem ele, o nome curto.</summary>
        public string Description => Model?.FullName ?? Name;
    }

    /// <summary>Desenho do cabo (polilinha + textos vão a vão), usado no lançamento manual e no automático.</summary>
    public static class CableDrawing
    {
        /// <summary>Metragem e lances de cada tipo de cabo do espaço, em ordem de nome.</summary>
        public static List<CableTotal> Totals(Transaction tr, BlockTableRecord space, List<CableModel> catalog)
        {
            var totals = new Dictionary<string, CableTotal>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline poly || XDataTags.GetCableName(poly) is not string name) continue;

                if (!totals.TryGetValue(name, out CableTotal? total))
                {
                    total = new CableTotal { Name = name, Model = CableProvider.Find(catalog, name) };
                    totals[name] = total;
                }
                total.Runs++;
                total.Length += poly.Length;
            }
            return totals.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Layer do cabo: FIBRA_CABO_&lt;nome&gt; ou, no backbone, FIBRA_BACKBONE_&lt;nome&gt;.</summary>
        public static string LayerFor(CableModel cable, bool backbone = false) =>
            (backbone ? FiberSettings.BackboneLayerPrefix : FiberSettings.CableLayerPrefix) + CadHelpers.SanitizeName(cable.ShortName.Replace(" ", "_"));

        /// <summary>
        /// Largura da linha do backbone, em metros na escala 1:1000 (0,6 mm no papel): bem mais grossa que a do cabo de
        /// distribuição, na tela e na impressão, sem depender da espessura de linha (LWDISPLAY).
        /// </summary>
        public const double BackboneWidth = 0.6;

        /// <summary>
        /// Aparência da layer do cabo (Configurações > Cabos): cor própria ou a cor padrão dos cabos, tipo de linha e
        /// espessura. <paramref name="settings"/> = preferências a usar (padrão: as atuais).
        /// </summary>
        public static LayerStyle StyleFor(CableModel cable, UserSettings? settings = null, bool backbone = false)
        {
            UserSettings s = settings ?? UserSettings.Current;
            // Backbone: a cor dele (azul por padrão), para destacar da distribuição; o tipo de linha e a espessura são os do cabo
            return new LayerStyle(backbone ? s.BackboneColor : cable.Color ?? s.CableColor, cable.Linetype, cable.LineWeightMm);
        }

        /// <summary>Comprimento de cada vão de uma sequência de pontos.</summary>
        public static List<double> SpanLengths(IList<Point3d> points)
        {
            var spans = new List<double>();
            for (int i = 0; i < points.Count - 1; i++) spans.Add(points[i].DistanceTo(points[i + 1]));
            return spans;
        }

        /// <param name="vertices">Vértices da polilinha.</param>
        /// <param name="spanLengths">Metragem escrita em cada vão (no roteamento automático é a distância
        /// real entre postes, que difere um pouco da linha afastada 1,8 m).</param>
        /// <param name="backbone">Cabo tronco (Lançar Backbone): layer própria e linha mais grossa; no resto é um cabo como os outros.</param>
        public static Polyline Draw(Transaction tr, Database db, BlockTableRecord space,
            IList<Point3d> vertices, IList<double> spanLengths, CableModel cable, bool backbone = false)
        {
            string layer = LayerFor(cable, backbone);
            CadHelpers.EnsureLayer(tr, db, layer, StyleFor(cable, backbone: backbone));

            var poly = new Polyline { Layer = layer };
            for (int i = 0; i < vertices.Count; i++)
            {
                // Polyline 2D, ignorando o Z
                poly.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
            }
            if (backbone) poly.ConstantWidth = BackboneWidth * DrawingScale.Factor(db, ScaleItem.CableText);
            CadHelpers.Append(tr, space, poly);
            XDataTags.TagCable(tr, db, poly, cable.ShortName, backbone);

            LabelSpans(tr, space, vertices, spanLengths, cable.ShortName, layer);
            return poly;
        }

        /// <summary>
        /// Troca o tipo de um cabo já lançado, sem mexer no percurso: a polilinha vai para a layer do cabo novo com o nome
        /// dele no XData, e os textos do cabo (nome e metragens dos vãos, na layer do cabo antigo) vão junto, com o nome
        /// trocado. São os textos perto deste cabo e mais perto dele que de qualquer outro (veja CableSwap.Belongs).
        /// </summary>
        /// <param name="poly">Cabo a trocar (aberto para leitura ou escrita).</param>
        /// <param name="others">Percursos dos outros cabos do desenho.</param>
        /// <returns>Quantos textos foram junto.</returns>
        public static int ChangeType(Transaction tr, Database db, BlockTableRecord space, Polyline poly, CableModel cable,
            IList<IReadOnlyList<(double X, double Y)>> others)
        {
            string oldName = XDataTags.GetCableName(poly) ?? "";
            string oldLayer = poly.Layer;
            bool backbone = XDataTags.IsBackbone(poly);
            string newLayer = LayerFor(cable, backbone);
            CadHelpers.EnsureLayer(tr, db, newLayer, StyleFor(cable, backbone: backbone));
            IReadOnlyList<(double X, double Y)> path = Path(poly);

            int moved = 0;
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent || !ent.Layer.Equals(oldLayer, StringComparison.OrdinalIgnoreCase)) continue;
                string? content = ent is MText m ? m.Text : ent is DBText t ? t.TextString : null;
                if (content == null) continue;
                CableTextKind kind = CableSwap.Classify(content, oldName);
                if (kind == CableTextKind.None) continue;

                double height = ent is MText mt ? mt.TextHeight : ((DBText)ent).Height;
                if (TextCenter(ent) is not Point3d center ||
                    !CableSwap.Belongs((center.X, center.Y), path, others, 4 * height)) continue;

                ent.UpgradeOpen();
                ent.Layer = newLayer;
                if (kind == CableTextKind.Name)
                {
                    if (ent is MText name) name.Contents = cable.ShortName;
                    else ((DBText)ent).TextString = cable.ShortName;
                }
                moved++;
            }

            if (!poly.IsWriteEnabled) poly.UpgradeOpen();
            poly.Layer = newLayer;
            XDataTags.TagCable(tr, db, poly, cable.ShortName, backbone);
            return moved;
        }

        /// <summary>Vértices da polilinha, para as contas de distância.</summary>
        public static IReadOnlyList<(double X, double Y)> Path(Polyline poly) =>
            Enumerable.Range(0, poly.NumberOfVertices).Select(i => { Point2d p = poly.GetPoint2dAt(i); return (p.X, p.Y); }).ToList();

        private static Point3d? TextCenter(Entity text)
        {
            try
            {
                Extents3d e = text.GeometricExtents;
                return new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2, 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null; // Texto vazio
            }
        }

        /// <summary>Nome do cabo acima da linha (um vão sim, um vão não) e metragem abaixo.</summary>
        private static void LabelSpans(Transaction tr, BlockTableRecord space, IList<Point3d> vertices,
            IList<double> spanLengths, string cableName, string layer)
        {
            double gap = DrawingScale.LabelGap(space.Database, ScaleItem.CableText);
            double height = DrawingScale.TextHeight(space.Database, ScaleItem.CableText);

            for (int i = 0; i < vertices.Count - 1; i++)
            {
                Point3d p1 = vertices[i];
                Point3d p2 = vertices[i + 1];
                if (p1.DistanceTo(p2) < 1e-6) continue;

                var mid = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
                double angle = CadHelpers.ReadableAngle(Math.Atan2(p2.Y - p1.Y, p2.X - p1.X));
                var up = new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0) * gap;

                if (i % 2 == 0)
                {
                    CadHelpers.AddText(tr, space, mid + up, cableName, angle, AttachmentPoint.BottomCenter, layer, height);
                }
                CadHelpers.AddText(tr, space, mid - up, $"{spanLengths[i]:F1}m", angle, AttachmentPoint.TopCenter, layer, height);
            }
        }
    }
}
