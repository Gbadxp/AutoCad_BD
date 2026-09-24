using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>Desenho do cabo (polilinha + textos vão a vão), usado no lançamento manual e no automático.</summary>
    public static class CableDrawing
    {
        public static string LayerFor(CableModel cable)
        {
            return FiberSettings.CableLayerPrefix + CadHelpers.SanitizeName(cable.ShortName.Replace(" ", "_"));
        }

        /// <param name="vertices">Vértices da polilinha.</param>
        /// <param name="spanLengths">Metragem escrita em cada vão (no roteamento automático é a distância
        /// real entre postes, que difere um pouco da linha afastada 1,8 m).</param>
        public static Polyline Draw(Transaction tr, Database db, BlockTableRecord space,
            IList<Point3d> vertices, IList<double> spanLengths, CableModel cable)
        {
            string layer = LayerFor(cable);
            CadHelpers.EnsureLayer(tr, db, layer, 3); // 3 = Verde

            var poly = new Polyline();
            for (int i = 0; i < vertices.Count; i++)
            {
                // Polyline 2D, ignorando o Z
                poly.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
            }
            poly.Layer = layer;

            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
            XDataTags.TagCable(tr, db, poly, cable.ShortName);

            LabelSpans(tr, space, vertices, spanLengths, cable.ShortName, layer);
            return poly;
        }

        /// <summary>Nome do cabo acima da linha (um vão sim, um vão não) e metragem abaixo.</summary>
        private static void LabelSpans(Transaction tr, BlockTableRecord space, IList<Point3d> vertices,
            IList<double> spanLengths, string cableName, string layer)
        {
            for (int i = 0; i < vertices.Count - 1; i++)
            {
                Point3d p1 = vertices[i];
                Point3d p2 = vertices[i + 1];
                if (p1.DistanceTo(p2) < 1e-6) continue;

                var mid = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
                double angle = CadHelpers.ReadableAngle(Math.Atan2(p2.Y - p1.Y, p2.X - p1.X));
                var up = new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0) * FiberSettings.LabelGap;

                if (i % 2 == 0)
                {
                    CadHelpers.AddText(tr, space, mid + up, cableName, angle, AttachmentPoint.BottomCenter, layer);
                }
                CadHelpers.AddText(tr, space, mid - up, $"{spanLengths[i]:F1}m", angle, AttachmentPoint.TopCenter, layer);
            }
        }
    }
}
