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

        /// <summary>
        /// Onde a seta está hoje (acompanha COPY, MOVE e colar), que pode não ser mais o <see cref="Point"/> gravado no
        /// cálculo. Null se não dá para saber (seta desenhada sem bloco por versões anteriores à 1.9.53).
        /// </summary>
        public Point3d? Current { get; set; }
    }

    /// <summary>
    /// Coloca a seta de esforço num poste, só onde a NDU 009 pede (fim de rede e deflexão acima de 10°, com resultante
    /// não nula; veja EffortResult.NeedsArrow). Antes, apaga a seta anterior do mesmo poste: recalcular não duplica setas
    /// e tira as que ficaram em poste de passagem.
    /// </summary>
    public class EffortMarkers
    {
        private const double SamePointTolerance = 0.01;

        // Tags do bloco "SETA DE ESFORÇO" (o "336" é o do modelo de projeto)
        private static readonly string[] EffortTags = { "ESFORÇO_KFG", "ESFORCO_KFG" };
        private static readonly string[] AngleTags = { "336", "ANGULO" };

        private readonly Transaction _tr;
        private readonly Database _db;
        private readonly BlockTableRecord _space;
        private readonly ObjectId _arrowBlockId;
        private readonly double _scale; // Fator da escala do desenho (1,0 em 1:1000)
        private readonly double _attachHeight;
        // Setas já desenhadas: ponto de onde saem e handle do poste a que pertencem ("" = sem poste ou versão antiga)
        private readonly List<(ObjectId Id, Point3d Pole, string PoleHandle)> _existing = new List<(ObjectId, Point3d, string)>();

        /// <summary>Setas colocadas.</summary>
        public int Placed { get; private set; }

        /// <summary>Pontos calculados que ficaram sem seta, porque a norma dispensa.</summary>
        public int Skipped { get; private set; }

        /// <summary>Desses, os que tinham seta de um cálculo anterior, apagada agora.</summary>
        public int Cleared { get; private set; }

        /// <param name="arrowBlockId">Bloco "SETA DE ESFORÇO" (ObjectId.Null para desenhar a seta e os textos).</param>
        /// <param name="poles">Postes do espaço (null = levantados aqui), para saber de que poste é cada seta já desenhada.</param>
        public EffortMarkers(Transaction tr, Database db, BlockTableRecord space, ObjectId arrowBlockId, IList<PoleInfo>? poles = null)
        {
            _tr = tr;
            _db = db;
            _space = space;
            _arrowBlockId = arrowBlockId;
            _scale = DrawingScale.Factor(db, ScaleItem.Effort);
            _attachHeight = CalcSettings.Get(db).AttachHeightM;

            CadHelpers.EnsureLayer(tr, db, FiberSettings.EffortLayer, UserSettings.Current.EffortColor);

            poles ??= Poles.Collect(tr, space);
            var byHandle = Poles.ByHandle(poles);
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                if (XDataTags.ReadEffortMarker(ent) is EffortMarkerData data)
                {
                    var (point, owner) = Locate(ent, data, poles, byHandle);
                    _existing.Add((id, point, owner?.Id.Handle.ToString() ?? ""));
                }
                else if (XDataTags.TryGetEffortPole(ent, out Point3d pole))
                {
                    _existing.Add((id, pole, ""));
                }
                else if (ent is BlockReference br &&
                         CadHelpers.GetBlockName(tr, br).Equals(FiberSettings.EffortBlockName, StringComparison.OrdinalIgnoreCase))
                {
                    // Setas criadas por versões antigas (sem XData): o ponto de inserção é o poste
                    _existing.Add((id, br.Position, ""));
                }
            }
        }

        /// <summary>
        /// Ponto e poste de uma seta já desenhada. A seta copiada (COPY, colar) leva os dados da original: o ponto do
        /// cálculo e o handle do poste de lá. Se ela está hoje em outro lugar e tem poste ali, é a cópia (ou foi movida
        /// junto com o poste): vale o lugar atual e esse poste. O handle gravado só vale com o poste perto do ponto ou
        /// sendo o mais perto dele; longe, é poste de outro desenho com o mesmo handle (colado).
        /// </summary>
        public static (Point3d Point, PoleInfo? Pole) Locate(Entity ent, EffortMarkerData data, IList<PoleInfo> poles,
            IDictionary<string, PoleInfo> byHandle)
        {
            Point3d? now = data.Current;
            if (now == null && Center(ent) is Point3d center)
            {
                // Seta sem bloco de versão anterior à 1.9.53: bem longe do ponto do cálculo, foi copiada ou movida; é do
                // poste perto dela, se houver (sem ele, de nenhum: não pode levar junto a seta do poste original)
                double reach = (FiberSettings.EffortArrowGap + FiberSettings.EffortArrowLength) * 2 *
                               Math.Max(1.0, DrawingScale.Factor(ent.Database, ScaleItem.Effort));
                if (center.DistanceTo(data.Point) > reach)
                    return Poles.Nearest(poles, center, reach) is PoleInfo near ? (near.Position, near) : (center, null);
            }

            if (now is Point3d current && current.DistanceTo(data.Point) > SamePointTolerance &&
                Poles.Nearest(poles, current, FiberSettings.PoleLinkRadius) is PoleInfo there)
            {
                return (current, there);
            }

            PoleInfo? linked = data.PoleHandle.Length > 0 && byHandle.TryGetValue(data.PoleHandle, out PoleInfo? p) ? p : null;
            if (linked != null && linked.Position.DistanceTo(data.Point) > FiberSettings.PoleLinkRadius &&
                Poles.Nearest(poles, data.Point, double.MaxValue) != linked)
            {
                linked = null;
            }
            return (data.Point, linked);
        }

        private static Point3d? Center(Entity ent)
        {
            try
            {
                Extents3d e = ent.GeometricExtents;
                return new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2, 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Coloca a seta no ponto quando a norma pede (EffortResult.NeedsArrow). Quando não pede, só apaga a seta que já
        /// estava ali, de um cálculo anterior. O esforço é devolvido nos dois casos, para as mensagens do comando.
        /// A seta anterior do mesmo poste sai mesmo fora do centro dele (ex.: Esforço de 1 Cabo, no ponto clicado).
        /// </summary>
        /// <param name="pole">Ponto de onde sai a seta (centro do poste vinculado, ou o ponto do cabo).</param>
        /// <param name="linkedPole">Poste a que o cálculo pertence; fica gravado na seta para o relatório.</param>
        /// <param name="clearWhenNotNeeded">False: onde a norma dispensa a seta, a que já estava fica (modo Cabo).</param>
        /// <returns>Esforço do poste comparado com o nominal.</returns>
        public PoleLoad Place(Point3d pole, EffortResult result, PoleInfo? linkedPole = null, bool clearWhenNotNeeded = true)
        {
            string handle = linkedPole?.Id.Handle.ToString() ?? "";
            PoleLoad load = PoleLoad.For(linkedPole, result.Kgf, _attachHeight);
            if (!result.NeedsArrow)
            {
                Skipped++;
                if (clearWhenNotNeeded) Clear(pole, handle);
                return load;
            }
            RemoveAt(pole, handle);

            // Mesmo formato do modelo de projeto: "24.98 KGF" / "ANG. 12°". O valor é o transferido a
            // 20 cm do topo (NDU 009, item 16.3 h); sem o poste, o da altura do cabo.
            double angle = result.AngleRad;
            string effortText = load.ProjectKgf.ToString("F2", CultureInfo.InvariantCulture) + " KGF";
            string angleText = "ANG. " + NormalizeDegrees(angle).ToString("F0", CultureInfo.InvariantCulture) + "°";

            var created = new List<Entity>();

            if (!_arrowBlockId.IsNull)
            {
                // Bloco "SETA DE ESFORÇO" do desenho ou da biblioteca
                created.Add(CadHelpers.InsertBlock(_tr, _space, _arrowBlockId, pole, angle, FiberSettings.EffortLayer, tag =>
                    EffortTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? effortText
                    : AngleTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? angleText
                    : null, _scale));
            }
            else
            {
                created.AddRange(DrawArrow(pole, angle, effortText, angleText));
            }
            Placed++;

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
                _existing.Add((ent.ObjectId, pole, data.PoleHandle));
            }
            return load;
        }

        /// <summary>
        /// Seta saindo do poste no sentido da resultante, com o esforço acima e o ângulo abaixo,
        /// os dois textos alinhados com a seta (sem ficar de cabeça para baixo).
        /// </summary>
        private IEnumerable<Entity> DrawArrow(Point3d pole, double angle, string effortText, string angleText)
        {
            var dir = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
            double length = FiberSettings.EffortArrowLength * _scale;
            double headLength = FiberSettings.EffortArrowHeadLength * _scale;

            Point3d tail = pole + dir * (FiberSettings.EffortArrowGap * _scale);
            Point3d tip = tail + dir * length;
            Point3d headBase = tip - dir * headLength;

            var arrow = new Polyline { Layer = FiberSettings.EffortLayer };
            arrow.AddVertexAt(0, new Point2d(tail.X, tail.Y), 0, 0, 0);
            arrow.AddVertexAt(1, new Point2d(headBase.X, headBase.Y), 0, FiberSettings.EffortArrowHeadWidth * _scale, 0);
            arrow.AddVertexAt(2, new Point2d(tip.X, tip.Y), 0, 0, 0);
            yield return CadHelpers.Append(_tr, _space, arrow);

            // Textos centralizados na haste, acima e abaixo dela
            Point3d mid = tail + dir * ((length - headLength) / 2.0);
            double textAngle = CadHelpers.ReadableAngle(angle);
            var up = new Vector3d(-Math.Sin(textAngle), Math.Cos(textAngle), 0) * (FiberSettings.LabelGap * _scale);

            double height = FiberSettings.TextHeight * _scale;
            yield return CadHelpers.AddText(_tr, _space, mid + up, effortText, textAngle, AttachmentPoint.BottomCenter, FiberSettings.EffortLayer, height);
            yield return CadHelpers.AddText(_tr, _space, mid - up, angleText, textAngle, AttachmentPoint.TopCenter, FiberSettings.EffortLayer, height);
        }

        private static double NormalizeDegrees(double radians)
        {
            double deg = radians * 180.0 / Math.PI;
            deg = ((deg % 360.0) + 360.0) % 360.0;
            return Math.Round(deg) >= 360 ? 0 : deg;
        }

        /// <summary>Apaga a seta que estava no ponto ou no poste, sem calcular nada (conta em Cleared).</summary>
        /// <param name="poleHandle">Handle do poste (null ou "" = só a do ponto).</param>
        public void Clear(Point3d point, string? poleHandle)
        {
            if (RemoveAt(point, poleHandle)) Cleared++;
        }

        /// <summary>Apaga a seta e os textos que já estavam no ponto ou que pertencem ao poste. True se havia algum.</summary>
        private bool RemoveAt(Point3d pole, string? poleHandle)
        {
            bool removed = false;
            for (int i = _existing.Count - 1; i >= 0; i--)
            {
                bool samePole = !string.IsNullOrEmpty(poleHandle) &&
                                string.Equals(_existing[i].PoleHandle, poleHandle, StringComparison.OrdinalIgnoreCase);
                if (!samePole && _existing[i].Pole.DistanceTo(pole) > SamePointTolerance) continue;

                var ent = (Entity)_tr.GetObject(_existing[i].Id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase();
                _existing.RemoveAt(i);
                removed = true;
            }
            return removed;
        }
    }
}
