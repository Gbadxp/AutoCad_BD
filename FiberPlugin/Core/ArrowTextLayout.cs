namespace FiberPlugin.Core
{
    /// <summary>Retângulo alinhado aos eixos (extensão de um texto ou de um desenho), em coordenadas do desenho.</summary>
    public readonly struct TextBounds
    {
        public TextBounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public TextBounds Moved(double dx, double dy) => new TextBounds(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);
    }

    /// <summary>
    /// Lugar da seta de esforço que não deixa os textos dela encostarem em outros textos nem no desenho de postes e
    /// caixas. A seta fica sempre no formato do projeto, com o valor em cima e o ângulo embaixo ("24.98 KGF" /
    /// "ANG. 12°"): os textos nunca mudam de lugar em relação à seta. Quando encostam, o conjunto todo anda mais para
    /// a ponta, na direção da seta, até um comprimento de seta; sem lugar livre, fica onde estava.
    /// </summary>
    public static class ArrowTextLayout
    {
        /// <summary>Passos do deslizamento do conjunto, em comprimentos da seta.</summary>
        private static readonly double[] Steps = { 0, 0.25, 0.5, 0.75, 1.0 };

        /// <param name="texts">Textos da seta, já na direção dela (eixo X ao longo da seta).</param>
        /// <param name="obstacles">
        /// Textos e desenhos em que eles não podem encostar, pelos quatro cantos e na mesma direção (um texto inclinado é
        /// um retângulo inclinado: a caixa reta em volta dele é bem maior que ele).
        /// </param>
        /// <param name="length">Comprimento da seta.</param>
        /// <returns>Quanto o conjunto anda ao longo da seta (0 se já está livre); null se não há lugar livre.</returns>
        public static double? Slide(IList<TextBounds> texts, IList<IReadOnlyList<(double X, double Y)>> obstacles, double length)
        {
            foreach (double step in Steps)
            {
                double along = step * length;
                if (!texts.Any(t => obstacles.Any(o => Overlaps(t.Moved(along, 0), o)))) return along;
            }
            return null;
        }

        /// <summary>Os quatro cantos do retângulo reto.</summary>
        public static IReadOnlyList<(double X, double Y)> Corners(TextBounds b) =>
            new[] { (b.MinX, b.MinY), (b.MaxX, b.MinY), (b.MaxX, b.MaxY), (b.MinX, b.MaxY) };

        /// <summary>
        /// O retângulo reto encosta no polígono convexo (ex.: retângulo inclinado)? Teste dos eixos separadores: estão
        /// separados se, em algum dos eixos (os dois do retângulo e a perpendicular de cada lado do polígono), as sombras
        /// dos dois não se cruzam. Só encostar na borda não conta.
        /// </summary>
        public static bool Overlaps(TextBounds box, IReadOnlyList<(double X, double Y)> polygon)
        {
            if (polygon.Count == 0) return false;
            if (polygon.Max(p => p.X) <= box.MinX || polygon.Min(p => p.X) >= box.MaxX ||
                polygon.Max(p => p.Y) <= box.MinY || polygon.Min(p => p.Y) >= box.MaxY) return false;

            IReadOnlyList<(double X, double Y)> corners = Corners(box);
            for (int i = 0; i < polygon.Count; i++)
            {
                var (x1, y1) = polygon[i];
                var (x2, y2) = polygon[(i + 1) % polygon.Count];
                double nx = y1 - y2, ny = x2 - x1; // Perpendicular ao lado
                if (Math.Abs(nx) < 1e-12 && Math.Abs(ny) < 1e-12) continue;
                double minP = polygon.Min(p => p.X * nx + p.Y * ny), maxP = polygon.Max(p => p.X * nx + p.Y * ny);
                double minB = corners.Min(p => p.X * nx + p.Y * ny), maxB = corners.Max(p => p.X * nx + p.Y * ny);
                if (maxB <= minP || minB >= maxP) return false;
            }
            return true;
        }
    }
}
