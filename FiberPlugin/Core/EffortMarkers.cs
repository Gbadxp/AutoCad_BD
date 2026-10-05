using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Dados gravados em cada seta de esforço (lidos pelo relatório).</summary>
    public class EffortMarkerData
    {
        public Point3d Point { get; set; }           // De onde sai a seta (centro do poste vinculado)
        public double Kgf { get; set; }
        public double AngleDeg { get; set; }
        public string Situation { get; set; } = ""; // Fim de rede, Ângulo (x°), Passagem
        public string PoleHandle { get; set; } = ""; // Handle do bloco do poste vinculado ("" = sem poste)
        public int CableCount { get; set; }

        /// <summary>Esforço transferido a 20 cm do topo, em kgf (null: sem poste ou altura desconhecida).</summary>
        public double? TopKgf { get; set; }
    }

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
        private readonly double _scale; // Fator da escala do desenho (1,0 em 1:1000)
        private readonly double _attachHeight;
        private readonly List<(ObjectId Id, Point3d Pole)> _existing = new List<(ObjectId, Point3d)>();

        /// <param name="arrowBlockId">Bloco "SETA DE ESFORÇO" (ObjectId.Null para usar texto).</param>
        public EffortMarkers(Transaction tr, Database db, BlockTableRecord space, ObjectId arrowBlockId)
        {
            _tr = tr;
            _db = db;
            _space = space;
            _arrowBlockId = arrowBlockId;
            _scale = DrawingScale.Factor(db);
            _attachHeight = CalcSettings.Get(db).AttachHeightM;

            CadHelpers.EnsureLayer(tr, db, FiberSettings.EffortLayer, UserSettings.Current.EffortColor);

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

        /// <param name="pole">Ponto de onde sai a seta (centro do poste vinculado, ou o ponto do cabo).</param>
        /// <param name="linkedPole">Poste a que o cálculo pertence; fica gravado na seta para o relatório.</param>
        /// <param name="angleOverride">Direção usada quando o esforço é nulo (poste em alinhamento reto).</param>
        /// <returns>Esforço do poste comparado com o nominal, para as mensagens do comando.</returns>
        public PoleLoad Place(Point3d pole, EffortResult result, PoleInfo? linkedPole = null, double? angleOverride = null)
        {
            RemoveAt(pole);
            PoleLoad load = PoleLoad.For(linkedPole, result.Kgf, _attachHeight);

            bool hasEffort = result.Kgf > 0.1;
            double angle = hasEffort ? result.AngleRad : angleOverride ?? 0;

            // Mesmo formato do modelo de projeto: "24.98 KGF" / "ANG. 12°". O valor é o transferido a
            // 20 cm do topo (NDU 009, item 16.3 h); sem o poste, o da altura do cabo.
            string effortText = load.ProjectKgf.ToString("F2", CultureInfo.InvariantCulture) + " KGF";
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
                }, _scale));
            }
            else
            {
                created.AddRange(DrawArrow(pole, angle, hasEffort, effortText, angleText));
            }

            var data = new EffortMarkerData
            {
                Point = pole,
                Kgf = result.Kgf,
                AngleDeg = NormalizeDegrees(angle),
                Situation = result.Situation,
                PoleHandle = linkedPole?.Id.Handle.ToString() ?? "",
                CableCount = result.CableCount,
                TopKgf = load.TopKgf
            };

            foreach (Entity ent in created)
            {
                XDataTags.TagEffortMarker(_tr, _db, ent, data);
                _existing.Add((ent.ObjectId, pole));
            }
            return load;
        }

        /// <summary>
        /// Seta saindo do poste no sentido da resultante, com o esforço acima e o ângulo abaixo,
        /// os dois textos alinhados com a seta (sem ficar de cabeça para baixo).
        /// </summary>
        private IEnumerable<Entity> DrawArrow(Point3d pole, double angle, bool hasEffort, string effortText, string angleText)
        {
            var dir = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            double length = FiberSettings.EffortArrowLength * _scale;
            double headLength = FiberSettings.EffortArrowHeadLength * _scale;

            Point3d tail = pole + dir * (FiberSettings.EffortArrowGap * _scale);
            Point3d tip = tail + dir * length;
            Point3d headBase = tip - dir * headLength;

            // Sem esforço (alinhamento reto) não há sentido para indicar: só os textos
            if (hasEffort)
            {
                var arrow = new Polyline { Layer = FiberSettings.EffortLayer };
                arrow.AddVertexAt(0, new Point2d(tail.X, tail.Y), 0, 0, 0);
                arrow.AddVertexAt(1, new Point2d(headBase.X, headBase.Y), 0, FiberSettings.EffortArrowHeadWidth * _scale, 0);
                arrow.AddVertexAt(2, new Point2d(tip.X, tip.Y), 0, 0, 0);
                yield return CadHelpers.Append(_tr, _space, arrow);
            }

            // Textos centralizados na haste, acima e abaixo dela
            Point3d mid = tail + dir * ((length - headLength) / 2.0);
            double textAngle = CadHelpers.ReadableAngle(angle);
            var up = new Vector3d(-Math.Sin(textAngle), Math.Cos(textAngle), 0) * (FiberSettings.LabelGap * _scale);

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
