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
    /// Coloca a seta de esforço num poste, só onde a NDU 009 pede (fim de rede e deflexão acima de 10°, com resultante
    /// não nula; veja EffortResult.NeedsArrow). Antes, apaga a seta anterior do mesmo poste: recalcular não duplica setas
    /// e tira as que ficaram em poste de passagem.
    /// </summary>
    public class EffortMarkers
    {
        private const double SamePointTolerance = 0.01;
        private static readonly CultureInfo Br = new CultureInfo("pt-BR");

        // Tags do bloco "SETA DE ESFORÇO" (o "336" é o do modelo de projeto)
        private static readonly string[] EffortTags = { "ESFORÇO_KFG", "ESFORCO_KFG" };
        private static readonly string[] AngleTags = { "336", "ANGULO" };

        private readonly Transaction _tr;
        private readonly Database _db;
        private readonly BlockTableRecord _space;
        private readonly ObjectId _arrowBlockId;
        private readonly double _scale; // Fator da escala do desenho (1,0 em 1:1000)
        private readonly double _attachHeight;
        private readonly string _blockBeta = "β";
        private readonly List<(ObjectId Id, Point3d Pole)> _existing = new List<(ObjectId, Point3d)>();

        /// <summary>Setas colocadas.</summary>
        public int Placed { get; private set; }

        /// <summary>Pontos calculados que ficaram sem seta, porque a norma dispensa.</summary>
        public int Skipped { get; private set; }

        /// <summary>Desses, os que tinham seta de um cálculo anterior, apagada agora.</summary>
        public int Cleared { get; private set; }

        /// <param name="arrowBlockId">Bloco "SETA DE ESFORÇO" (ObjectId.Null para desenhar a seta e os textos).</param>
        public EffortMarkers(Transaction tr, Database db, BlockTableRecord space, ObjectId arrowBlockId)
        {
            _tr = tr;
            _db = db;
            _space = space;
            _arrowBlockId = arrowBlockId;
            _scale = DrawingScale.Factor(db);
            _attachHeight = CalcSettings.Get(db).AttachHeightM;
            if (!arrowBlockId.IsNull) _blockBeta = BetaFor(AngleAttributeStyle(arrowBlockId));

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

        /// <summary>
        /// Coloca a seta no ponto quando a norma pede (EffortResult.NeedsArrow). Quando não pede, só apaga a seta que já
        /// estava ali, de um cálculo anterior. O esforço é devolvido nos dois casos, para as mensagens do comando.
        /// </summary>
        /// <param name="pole">Ponto de onde sai a seta (centro do poste vinculado, ou o ponto do cabo).</param>
        /// <param name="linkedPole">Poste a que o cálculo pertence; fica gravado na seta para o relatório.</param>
        /// <returns>Esforço do poste comparado com o nominal.</returns>
        public PoleLoad Place(Point3d pole, EffortResult result, PoleInfo? linkedPole = null)
        {
            bool removed = RemoveAt(pole);
            PoleLoad load = PoleLoad.For(linkedPole, result.Kgf, _attachHeight);
            if (!result.NeedsArrow)
            {
                Skipped++;
                if (removed) Cleared++;
                return load;
            }

            // Símbolo "Indicação de esforço resultante/ângulo" do Anexo C da NDU 009: "E= 24,50 daN" e "β= 12°". O valor é
            // o transferido a 20 cm do topo (item 16.3 h); sem o poste, o da altura do cabo. β é a direção da resultante
            // (anti-horário a partir do leste) e a seta dá o sentido.
            double angle = result.AngleRad;
            string effortText = "E= " + load.ProjectDaN.ToString("F2", Br) + " daN";
            string degrees = "= " + NormalizeDegrees(angle).ToString("F0", Br) + "°";

            var created = new List<Entity>();

            if (!_arrowBlockId.IsNull)
            {
                // Bloco "SETA DE ESFORÇO" do desenho ou da biblioteca
                created.Add(CadHelpers.InsertBlock(_tr, _space, _arrowBlockId, pole, angle, FiberSettings.EffortLayer, tag =>
                    EffortTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? effortText
                    : AngleTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? _blockBeta + degrees
                    : null, _scale));
            }
            else
            {
                // O MText troca para Arial só no β, que as fontes SHX não têm
                created.AddRange(DrawArrow(pole, angle, effortText, @"{\fArial|b0|i0|c0|p34;β}" + degrees));
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
                _existing.Add((ent.ObjectId, pole));
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

            yield return CadHelpers.AddText(_tr, _space, mid + up, effortText, textAngle, AttachmentPoint.BottomCenter, FiberSettings.EffortLayer);
            yield return CadHelpers.AddText(_tr, _space, mid - up, angleText, textAngle, AttachmentPoint.TopCenter, FiberSettings.EffortLayer);
        }

        private static double NormalizeDegrees(double radians)
        {
            double deg = radians * 180.0 / Math.PI;
            deg = ((deg % 360.0) + 360.0) % 360.0;
            return Math.Round(deg) >= 360 ? 0 : deg;
        }

        /// <summary>Apaga a seta que estava no ponto, sem calcular nada (conta em Cleared).</summary>
        public void Clear(Point3d point)
        {
            if (RemoveAt(point)) Cleared++;
        }

        /// <summary>Apaga a seta e os textos que já estavam no ponto. True se havia algum.</summary>
        private bool RemoveAt(Point3d pole)
        {
            bool removed = false;
            for (int i = _existing.Count - 1; i >= 0; i--)
            {
                if (_existing[i].Pole.DistanceTo(pole) > SamePointTolerance) continue;

                var ent = (Entity)_tr.GetObject(_existing[i].Id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase();
                _existing.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        /// <summary>Estilo de texto do atributo do ângulo no bloco da seta (Null se o bloco não tiver).</summary>
        private ObjectId AngleAttributeStyle(ObjectId blockId)
        {
            var block = (BlockTableRecord)_tr.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId id in block)
            {
                if (_tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition att &&
                    AngleTags.Contains(att.Tag.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return att.TextStyleId;
                }
            }
            return ObjectId.Null;
        }

        /// <summary>
        /// "β" quando a fonte do estilo é TrueType. As fontes SHX não têm letras gregas (o β sairia "?"): nelas vai "b",
        /// como no símbolo impresso no Anexo C da norma.
        /// </summary>
        private string BetaFor(ObjectId textStyleId)
        {
            if (textStyleId.IsNull || _tr.GetObject(textStyleId, OpenMode.ForRead) is not TextStyleTableRecord style) return "β";
            string file = style.FileName ?? "";
            bool trueType = !string.IsNullOrEmpty(style.Font.TypeFace) ||
                            new[] { ".ttf", ".ttc", ".otf" }.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            return trueType ? "β" : "b";
        }
    }
}
