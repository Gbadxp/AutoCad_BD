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
        public string Situation { get; set; } = ""; // Fim de rede, Derivação, Ângulo (x°), Passagem
        public string PoleHandle { get; set; } = ""; // Handle do bloco do poste vinculado ("" = sem poste)
        public int CableCount { get; set; }

        /// <summary>Esforço transferido a 20 cm do topo, em kgf (null: sem poste ou altura desconhecida).</summary>
        public double? TopKgf { get; set; }

        /// <summary>
        /// Onde a seta está hoje (acompanha COPY, MOVE e colar), que pode não ser mais o <see cref="Point"/> gravado no
        /// cálculo. Null se não dá para saber (seta desenhada sem bloco por versões anteriores à 1.9.53).
        /// </summary>
        public Point3d? Current { get; set; }

        /// <summary>
        /// Aviso de ponta ou ângulo de cabo sem poste (no lugar da seta, que pela norma é do poste): não conta como seta no
        /// relatório e sai ao recalcular, como as setas.
        /// </summary>
        public bool NoPole { get; set; }
    }

    /// <summary>
    /// Coloca a seta de esforço num poste, só onde a NDU 009 pede (fim de rede, derivação e deflexão acima de 10°, com resultante
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
        // Textos e desenho dos postes/caixas do espaço, em que os textos da seta não podem encostar
        // (quatro cantos: texto inclinado é um retângulo inclinado)
        private readonly List<Point3d[]> _obstacles = new List<Point3d[]>();

        /// <summary>Setas colocadas.</summary>
        public int Placed { get; private set; }

        /// <summary>Pontos calculados que ficaram sem seta, porque a norma dispensa.</summary>
        public int Skipped { get; private set; }

        /// <summary>Desses, os que tinham seta de um cálculo anterior, apagada agora.</summary>
        public int Cleared { get; private set; }

        /// <summary>Pontas e ângulos de cabo sem poste, marcados com o aviso "SEM POSTE" no lugar da seta.</summary>
        public int WithoutPole { get; private set; }

        /// <summary>Layer do aviso "SEM POSTE" (vermelha, não imprime).</summary>
        public const string NoPoleLayer = "FIBRA_ESFORCO_SEM_POSTE";

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
                else if (ent is MText || ent is DBText ||
                         ent is BlockReference symbol && (XDataTags.ReadPole(symbol) != null || XDataTags.ReadBox(symbol) != null))
                {
                    if (Corners(ent) is Point3d[] corners) _obstacles.Add(corners);
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

            double angle = result.AngleRad;
            var data = new EffortMarkerData
            {
                Point = pole,
                Kgf = result.Kgf,
                AngleDeg = NormalizeDegrees(angle),
                Situation = result.Situation,
                PoleHandle = handle,
                CableCount = result.CableCount,
                TopKgf = load.TopKgf,
                NoPole = linkedPole == null
            };

            var created = new List<Entity>();
            if (linkedPole == null)
            {
                // Pela norma a seta é do poste: aqui, sem poste, fica o aviso para inserir o poste (não imprime)
                created.AddRange(DrawNoPoleWarning(pole, result, load));
                WithoutPole++;
            }
            else
            {
                // Mesmo formato do modelo de projeto: "24.98 KGF" / "ANG. 12°". O valor é o transferido a
                // 20 cm do topo (NDU 009, item 16.3 h).
                string effortText = load.ProjectKgf.ToString("F2", CultureInfo.InvariantCulture) + " KGF";
                string angleText = "ANG. " + NormalizeDegrees(angle).ToString("F0", CultureInfo.InvariantCulture) + "°";
                List<Entity> texts;
                if (!_arrowBlockId.IsNull)
                {
                    // Bloco "SETA DE ESFORÇO" do desenho ou da biblioteca; os textos são os atributos dele
                    BlockReference arrow = CadHelpers.InsertBlock(_tr, _space, _arrowBlockId, pole, angle, FiberSettings.EffortLayer, tag =>
                        EffortTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? effortText
                        : AngleTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? angleText
                        : null, _scale);
                    created.Add(arrow);
                    texts = arrow.AttributeCollection.Cast<ObjectId>()
                        .Select(id => (AttributeReference)_tr.GetObject(id, OpenMode.ForWrite))
                        .Where(a => EffortTags.Contains(a.Tag.Trim(), StringComparer.OrdinalIgnoreCase) ||
                                    AngleTags.Contains(a.Tag.Trim(), StringComparer.OrdinalIgnoreCase))
                        .Cast<Entity>().ToList();
                }
                else
                {
                    created.AddRange(DrawArrow(pole, angle, effortText, angleText));
                    texts = created.OfType<MText>().Cast<Entity>().ToList();
                }
                AvoidOverlap(created, texts, pole, angle);
                foreach (Entity text in texts)
                {
                    if (Corners(text) is Point3d[] corners) _obstacles.Add(corners); // A próxima seta também desvia destes textos
                }
                Placed++;
            }

            foreach (Entity ent in created)
            {
                XDataTags.TagEffortMarker(_tr, _db, ent, data);
                _existing.Add((ent.ObjectId, pole, data.PoleHandle));
            }
            return load;
        }

        /// <summary>
        /// Aviso no lugar da seta, onde o cabo termina ou faz ângulo sem poste: círculo vermelho e "SEM POSTE: insira o
        /// poste", numa layer que não imprime. Sai ao recalcular depois de inserir o poste.
        /// </summary>
        private IEnumerable<Entity> DrawNoPoleWarning(Point3d at, EffortResult result, PoleLoad load)
        {
            CadHelpers.EnsureNonPlottingLayer(_tr, _db, NoPoleLayer, 1);
            double radius = 2.5 * _scale, height = FiberSettings.TextHeight * _scale;
            yield return CadHelpers.Append(_tr, _space, new Circle(at, Vector3d.ZAxis, radius) { Layer = NoPoleLayer });
            string detail = $"{result.Situation}, {load.ProjectKgf.ToString("F2", CultureInfo.InvariantCulture)} kgf no cabo";
            yield return CadHelpers.AddText(_tr, _space, at + new Vector3d(radius + height * 0.5, radius, 0),
                CadHelpers.MTextLiteral("SEM POSTE: insira o poste") + "\\P" + CadHelpers.MTextLiteral(detail),
                0, AttachmentPoint.TopLeft, NoPoleLayer, height);
        }

        /// <summary>
        /// Seta cujos textos (esforço e ângulo) encostam em outro texto (do poste, do vão, de outra seta) ou no desenho de
        /// um poste/caixa: a seta inteira, com os textos, anda mais para a ponta até ficar livre (veja ArrowTextLayout). Os
        /// textos nunca mudam de lugar em relação à seta; sem lugar livre, tudo fica onde estava.
        /// </summary>
        /// <param name="arrow">Tudo o que forma a seta (bloco com os atributos, ou polilinha e textos).</param>
        /// <param name="texts">Os textos dela, que não podem encostar em nada.</param>
        private void AvoidOverlap(List<Entity> arrow, List<Entity> texts, Point3d pole, double angle)
        {
            // A conta é feita na direção da seta (eixo X ao longo dela): os textos da seta, inclinados junto com ela, ficam
            // com o tamanho real; a caixa reta em volta de um texto inclinado é bem maior que ele e empurrava os textos
            // para longe sem precisar
            Matrix3d toLocal = Matrix3d.Rotation(-angle, Vector3d.ZAxis, pole);
            var boxes = new List<TextBounds>();
            foreach (Entity text in texts)
            {
                if (AlignedExtents(text, toLocal) is Extents3d e) boxes.Add(Local(e, pole));
            }
            if (boxes.Count == 0) return;

            double length = FiberSettings.EffortArrowLength * _scale;
            double reach = 2 * (FiberSettings.EffortArrowGap * _scale + length);
            List<IReadOnlyList<(double X, double Y)>> near = _obstacles
                .Where(o => o.Min(p => p.DistanceTo(pole)) <= reach + Math.Max(o[0].DistanceTo(o[2]), o[1].DistanceTo(o[3])))
                .Select(o => (IReadOnlyList<(double X, double Y)>)o.Select(p =>
                {
                    Point3d local = p.TransformBy(toLocal);
                    return (local.X - pole.X, local.Y - pole.Y);
                }).ToList())
                .ToList();
            if (near.Count == 0) return;

            // Sem lugar livre, ou já livre: fica onde está. O bloco da seta leva os atributos junto
            if (ArrowTextLayout.Slide(boxes, near, length) is not double along || along <= 0) return;
            Matrix3d move = Matrix3d.Displacement(new Vector3d(Math.Cos(angle), Math.Sin(angle), 0) * along);
            foreach (Entity ent in arrow) ent.TransformBy(move);
        }

        /// <summary>
        /// Extensão do texto girado para a direção da seta (o tamanho real dele, sem a sobra da caixa reta de um texto
        /// inclinado), pela cópia girada; sem ela, a caixa reta girada (maior, mas certa). Null se o texto não tem extensão.
        /// </summary>
        private static Extents3d? AlignedExtents(Entity text, Matrix3d toLocal)
        {
            try
            {
                using (Entity aligned = text.GetTransformedCopy(toLocal))
                {
                    if (Extents(aligned) is Extents3d e) return e;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Cópia que o AutoCAD não mede fora do desenho: vale a caixa reta
            }
            if (Extents(text) is not Extents3d world) return null;
            Point3d a = world.MinPoint, b = world.MaxPoint;
            Point3d[] corners = new[] { a, new Point3d(b.X, a.Y, 0), b, new Point3d(a.X, b.Y, 0) }.Select(c => c.TransformBy(toLocal)).ToArray();
            return new Extents3d(new Point3d(corners.Min(c => c.X), corners.Min(c => c.Y), 0), new Point3d(corners.Max(c => c.X), corners.Max(c => c.Y), 0));
        }

        /// <summary>Caixa já na direção da seta (extensão girada em volta do poste), com o poste na origem.</summary>
        private static TextBounds Local(Extents3d aligned, Point3d pole) =>
            new TextBounds(aligned.MinPoint.X - pole.X, aligned.MinPoint.Y - pole.Y, aligned.MaxPoint.X - pole.X, aligned.MaxPoint.Y - pole.Y);

        /// <summary>
        /// Os quatro cantos do texto (o retângulo inclinado junto com ele) ou, nos outros desenhos, da caixa reta em volta.
        /// Null se não tem extensão.
        /// </summary>
        private static Point3d[]? Corners(Entity ent)
        {
            double rotation = ent is MText mtext ? mtext.Rotation : ent is DBText text ? text.Rotation : 0;
            if (Math.Abs(rotation) > 1e-9)
            {
                // Texto girado de volta para a horizontal: a extensão dele é o retângulo do texto, que gira de novo
                Matrix3d flat = Matrix3d.Rotation(-rotation, Vector3d.ZAxis, Point3d.Origin);
                try
                {
                    using (Entity copy = ent.GetTransformedCopy(flat))
                    {
                        if (Extents(copy) is Extents3d e) return RectangleCorners(e).Select(p => p.TransformBy(flat.Inverse())).ToArray();
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Cópia que o AutoCAD não mede fora do desenho: vale a caixa reta
                }
            }
            return Extents(ent) is Extents3d box ? RectangleCorners(box) : null;
        }

        private static Point3d[] RectangleCorners(Extents3d e) => new[]
        {
            e.MinPoint, new Point3d(e.MaxPoint.X, e.MinPoint.Y, 0), e.MaxPoint, new Point3d(e.MinPoint.X, e.MaxPoint.Y, 0)
        };

        private static Extents3d? Extents(Entity ent)
        {
            try
            {
                return ent.GeometricExtents;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null; // Texto vazio
            }
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
