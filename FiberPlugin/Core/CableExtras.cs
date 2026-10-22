using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Reserva técnica de cabo: seta vermelha apontando para o ponto da reserva (poste ou meio do vão), com
    /// "RESERVA TÉCNICA 30m" escrito ao longo dela. A ponta fica no ponto clicado (encostada no poste, quando é num poste)
    /// e o texto vai para o lado do segundo clique.
    /// </summary>
    public static class TechnicalReserve
    {
        public const string Layer = "FIBRA_RESERVA_TECNICA";
        public const short Color = 1; // Vermelho

        public static string Text(double meters) => "RESERVA TÉCNICA " + meters.ToString("0.#", CultureInfo.InvariantCulture) + "m";

        /// <summary>
        /// Pontos da seta: cauda, base da ponta e ponta. A seta sai de <paramref name="anchor"/> na direção de
        /// <paramref name="toward"/>, com o comprimento até ele mas nunca menor que <paramref name="minLength"/> (o texto
        /// tem que caber). <paramref name="poleGap"/> &gt; 0: o ponto é um poste e a ponta fica afastada dele, sem cobrir o
        /// desenho do poste.
        /// </summary>
        public static (Point3d Tail, Point3d HeadBase, Point3d Tip) Arrow(Point3d anchor, Point3d toward, double minLength,
            double headLength, double poleGap)
        {
            Vector3d v = new Vector3d(toward.X - anchor.X, toward.Y - anchor.Y, 0);
            Vector3d dir = v.Length > 1e-9 ? v.GetNormal() : new Vector3d(1, -1, 0).GetNormal(); // Sem direção: para baixo à direita
            Point3d tip = new Point3d(anchor.X, anchor.Y, 0) + dir * poleGap;
            double length = Math.Max(v.Length - poleGap, minLength);
            return (tip + dir * length, tip + dir * headLength, tip);
        }

        /// <summary>
        /// Seta e texto da reserva, ainda fora do desenho (a prévia do comando desenha estes mesmos). A layer precisa
        /// existir no desenho.
        /// </summary>
        /// <param name="atPole">O ponto é o centro de um poste.</param>
        public static List<Entity> Build(Database db, Point3d anchor, bool atPole, Point3d toward, double meters)
        {
            double factor = DrawingScale.Factor(db, ScaleItem.CableText);
            double height = FiberSettings.TextHeight * factor;
            double headLength = FiberSettings.EffortArrowHeadLength * factor;
            string text = Text(meters);
            // Largura aproximada do texto (fonte padrão), com folga de uma altura de cada lado
            double minLength = text.Length * 0.8 * height + 2 * height + headLength;
            double poleGap = atPole ? FiberSettings.EffortArrowGap * DrawingScale.Factor(db, ScaleItem.PoleIcon) : 0;
            var (tail, headBase, tip) = Arrow(anchor, toward, minLength, headLength, poleGap);

            var arrow = new Polyline { Layer = Layer };
            arrow.AddVertexAt(0, new Point2d(tail.X, tail.Y), 0, 0, 0);
            arrow.AddVertexAt(1, new Point2d(headBase.X, headBase.Y), 0, FiberSettings.EffortArrowHeadWidth * factor, 0);
            arrow.AddVertexAt(2, new Point2d(tip.X, tip.Y), 0, 0, 0);

            // Texto no meio da haste, do lado de cima na leitura (sem ficar de cabeça para baixo)
            double textAngle = CadHelpers.ReadableAngle(Math.Atan2(tip.Y - tail.Y, tip.X - tail.X));
            Point3d mid = tail + (headBase - tail) / 2;
            var up = new Vector3d(-Math.Sin(textAngle), Math.Cos(textAngle), 0) * (FiberSettings.LabelGap * factor);
            var label = new MText
            {
                Location = mid + up,
                Contents = CadHelpers.MTextLiteral(text),
                TextHeight = height,
                Rotation = textAngle,
                Attachment = AttachmentPoint.BottomCenter,
                Layer = Layer
            };
            return new List<Entity> { arrow, label };
        }
    }

    /// <summary>
    /// Cordoalha: linha laranja tracejada pelos pontos clicados, com a metragem de cada trecho em verde ("36.97").
    /// Não é cabo de fibra: não entra no esforço nem na metragem de cabos, só na própria metragem (Produção do Dia).
    /// </summary>
    public static class Strand
    {
        public const string Layer = "FIBRA_CORDOALHA";
        public const string TextLayer = "FIBRA_CORDOALHA_TEXTO";
        public static readonly LayerStyle Style = new LayerStyle(30, "DASHED"); // Laranja, tracejada
        public const short TextColor = 3;                                      // Verde

        /// <summary>Traço da linha no desenho, em metros na escala 1:1000 (o DASHED do acadiso.lin tem 12,7).</summary>
        private const double DashLength = 1.6;
        private const double LinetypeDash = 12.7;

        /// <summary>
        /// Largura da linha, em metros na escala 1:1000 (0,5 mm no papel). É a largura da polilinha, que aparece na tela e
        /// na impressão sem depender da espessura de linha (LWDISPLAY).
        /// </summary>
        private const double Width = 0.5;

        public static Polyline Draw(Transaction tr, Database db, BlockTableRecord space, IList<Point3d> points)
        {
            CadHelpers.EnsureLayer(tr, db, Layer, Style);
            CadHelpers.EnsureLayer(tr, db, TextLayer, TextColor);
            double factor = DrawingScale.Factor(db, ScaleItem.CableText);

            // Traço curto qualquer que seja a escala de tipo de linha do desenho (LTSCALE), e contínuo nos vértices
            var poly = new Polyline
            {
                Layer = Layer,
                LinetypeScale = DashLength * factor / LinetypeDash / (db.Ltscale > 1e-9 ? db.Ltscale : 1),
                Plinegen = true
            };
            for (int i = 0; i < points.Count; i++) poly.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);
            poly.ConstantWidth = Width * factor;
            CadHelpers.Append(tr, space, poly);
            XDataTags.TagStrand(tr, db, poly);

            double height = FiberSettings.TextHeight * factor;
            double gap = FiberSettings.LabelGap * factor;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Point3d p1 = points[i], p2 = points[i + 1];
                double length = p1.DistanceTo(p2);
                if (length < 1e-6) continue;

                var mid = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
                double angle = CadHelpers.ReadableAngle(Math.Atan2(p2.Y - p1.Y, p2.X - p1.X));
                var up = new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0) * gap;
                MText text = CadHelpers.AddText(tr, space, mid + up, length.ToString("F2", CultureInfo.InvariantCulture), angle,
                    AttachmentPoint.BottomCenter, TextLayer, height);
                XDataTags.TagStrand(tr, db, text);
            }
            return poly;
        }

        /// <summary>A polilinha é cordoalha: feita pelo comando (XData) ou desenhada à mão na layer da cordoalha.</summary>
        public static bool IsStrand(Polyline poly) =>
            XDataTags.IsStrand(poly) || poly.Layer.Equals(Layer, StringComparison.OrdinalIgnoreCase);
    }
}
