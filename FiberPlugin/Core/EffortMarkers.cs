using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Coloca a seta (ou texto) de esforço num poste. Antes de colocar, apaga a marcação anterior do
    /// mesmo poste, então recalcular não duplica setas.
    /// </summary>
    public class EffortMarkers
    {
        private const double SamePointTolerance = 0.01;

        private readonly Transaction _tr;
        private readonly Database _db;
        private readonly BlockTableRecord _space;
        private readonly ObjectId _arrowBlockId;
        private readonly List<(ObjectId Id, Point3d Pole)> _existing = new List<(ObjectId, Point3d)>();

        /// <param name="arrowBlockId">Bloco "SETA DE ESFORÇO" (ObjectId.Null para usar texto).</param>
        public EffortMarkers(Transaction tr, Database db, BlockTableRecord space, ObjectId arrowBlockId)
        {
            _tr = tr;
            _db = db;
            _space = space;
            _arrowBlockId = arrowBlockId;

            CadHelpers.EnsureLayer(tr, db, FiberSettings.EffortLayer, 4); // 4 = Ciano, como no modelo de projeto

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                if (XDataTags.TryGetEffortPole(ent, out Point3d pole))
                {
                    _existing.Add((id, pole));
                }
                else if (ent is BlockReference br &&
                         CadHelpers.GetBlockName(tr, br).Equals(FiberSettings.EffortBlockName, StringComparison.OrdinalIgnoreCase))
                {
                    // Setas criadas por versões antigas (sem XData): o ponto de inserção é o poste
                    _existing.Add((id, br.Position));
                }
            }
        }

        /// <param name="angleOverride">Direção usada quando o esforço é nulo (poste em alinhamento reto).</param>
        public void Place(Point3d pole, EffortResult result, double? angleOverride = null)
        {
            RemoveAt(pole);

            bool hasEffort = result.Kgf > 0.1;
            double angle = hasEffort ? result.AngleRad : angleOverride ?? 0;

            // Mesmo formato do modelo de projeto: "24.98 KGF" / "ANG. 12°"
            string effortText = result.Kgf.ToString("F2", CultureInfo.InvariantCulture) + " KGF";
            string angleText = "ANG. " + NormalizeDegrees(angle).ToString("F0", CultureInfo.InvariantCulture) + "°";

            var created = new List<Entity>();

            if (!_arrowBlockId.IsNull)
            {
                // Bloco "SETA DE ESFORÇO" do desenho ou da biblioteca
                created.Add(CadHelpers.InsertBlock(_tr, _space, _arrowBlockId, pole, angle, FiberSettings.EffortLayer, tag =>
                {
                    if (tag.Equals("ESFORÇO_KFG", StringComparison.OrdinalIgnoreCase) ||
                        tag.Equals("ESFORCO_KFG", StringComparison.OrdinalIgnoreCase))
                    {
                        return effortText;
                    }
                    if (tag.Equals("336", StringComparison.OrdinalIgnoreCase) ||
                        tag.Equals("ANGULO", StringComparison.OrdinalIgnoreCase))
                    {
                        return angleText;
                    }
                    return null;
                }));
            }
            else
            {
                created.AddRange(DrawArrow(pole, angle, hasEffort, effortText, angleText));
            }

            foreach (Entity ent in created)
            {
                XDataTags.TagEffortMarker(_tr, _db, ent, pole);
                _existing.Add((ent.ObjectId, pole));
            }
        }

        /// <summary>
        /// Seta saindo do poste no sentido da resultante, com o esforço acima e o ângulo abaixo,
        /// os dois textos alinhados com a seta (sem ficar de cabeça para baixo).
        /// </summary>
        private IEnumerable<Entity> DrawArrow(Point3d pole, double angle, bool hasEffort, string effortText, string angleText)
        {
            var dir = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            Point3d tail = pole + dir * FiberSettings.EffortArrowGap;
            Point3d tip = tail + dir * FiberSettings.EffortArrowLength;
            Point3d headBase = tip - dir * FiberSettings.EffortArrowHeadLength;

            // Sem esforço (alinhamento reto) não há sentido para indicar: só os textos
            if (hasEffort)
            {
                var arrow = new Polyline();
                arrow.AddVertexAt(0, new Point2d(tail.X, tail.Y), 0, 0, 0);
                arrow.AddVertexAt(1, new Point2d(headBase.X, headBase.Y), 0, FiberSettings.EffortArrowHeadWidth, 0);
                arrow.AddVertexAt(2, new Point2d(tip.X, tip.Y), 0, 0, 0);
                arrow.Layer = FiberSettings.EffortLayer;
                _space.AppendEntity(arrow);
                _tr.AddNewlyCreatedDBObject(arrow, true);
                yield return arrow;
            }

            // Textos centralizados na haste, acima e abaixo dela
            Point3d mid = tail + dir * ((FiberSettings.EffortArrowLength - FiberSettings.EffortArrowHeadLength) / 2.0);
            double textAngle = CadHelpers.ReadableAngle(angle);
            var up = new Vector3d(-Math.Sin(textAngle), Math.Cos(textAngle), 0) * FiberSettings.LabelGap;

            yield return CadHelpers.AddText(_tr, _space, mid + up, effortText, textAngle, AttachmentPoint.BottomCenter, FiberSettings.EffortLayer);
            yield return CadHelpers.AddText(_tr, _space, mid - up, angleText, textAngle, AttachmentPoint.TopCenter, FiberSettings.EffortLayer);
        }

        private static double NormalizeDegrees(double radians)
        {
            double deg = radians * 180.0 / Math.PI;
            deg = ((deg % 360.0) + 360.0) % 360.0;
            return Math.Round(deg) >= 360 ? 0 : deg;
        }

        private void RemoveAt(Point3d pole)
        {
            for (int i = _existing.Count - 1; i >= 0; i--)
            {
                if (_existing[i].Pole.DistanceTo(pole) > SamePointTolerance) continue;

                var ent = (Entity)_tr.GetObject(_existing[i].Id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase();
                _existing.RemoveAt(i);
            }
        }
    }
}
