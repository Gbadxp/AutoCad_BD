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

        public double CenterX => (MinX + MaxX) / 2;
        public double CenterY => (MinY + MaxY) / 2;

        public TextBounds Moved(double dx, double dy) => new TextBounds(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);

        public bool Overlaps(TextBounds other) =>
            MinX < other.MaxX && MaxX > other.MinX && MinY < other.MaxY && MaxY > other.MinY;

        /// <summary>Distância do ponto ao retângulo (zero dentro dele).</summary>
        public double DistanceTo(double x, double y)
        {
            double dx = Math.Max(0, Math.Max(MinX - x, x - MaxX));
            double dy = Math.Max(0, Math.Max(MinY - y, y - MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Lugar dos textos da seta de esforço (valor e ângulo) que não encosta em outros textos nem no desenho de postes e
    /// caixas. Tenta, do menor deslocamento para o maior: os textos onde estão, os dois empilhados do mesmo lado da seta
    /// (em cima ou embaixo), um pouco mais afastados da seta e mais para a ponta dela; sempre com o valor em cima do
    /// ângulo ("24.98 KGF" / "ANG. 12°"). Trocar os dois de lado inverte essa ordem, então só vale sem outro lugar.
    /// </summary>
    public static class ArrowTextLayout
    {
        /// <summary>Mesma arrumação com obstáculos retos (alinhados aos eixos).</summary>
        public static List<(double X, double Y)>? Arrange(IList<TextBounds> texts, IList<TextBounds> obstacles, double poleX, double poleY,
            double angle, double length, double textHeight, int upSide = 1) =>
            Arrange(texts, obstacles.Select(Corners).ToList(), poleX, poleY, angle, length, textHeight, upSide);

        /// <param name="texts">Textos da seta de cima para baixo, como devem ficar (o valor primeiro).</param>
        /// <param name="obstacles">
        /// Textos e desenhos em que eles não podem encostar, pelos quatro cantos (um texto inclinado é um retângulo
        /// inclinado: a caixa reta em volta dele é bem maior que ele).
        /// </param>
        /// <param name="angle">Direção da seta (radianos).</param>
        /// <param name="length">Comprimento da seta.</param>
        /// <param name="textHeight">Altura do texto da seta (passo dos afastamentos).</param>
        /// <param name="upSide">
        /// Lado da seta que fica para cima na leitura dos textos: 1 = o lado esquerdo de quem olha na direção da seta,
        /// -1 = o direito (seta apontando para a esquerda, com os textos girados para não ficar de cabeça para baixo).
        /// </param>
        /// <returns>Deslocamento de cada texto (zeros se já está livre); null se não há lugar livre.</returns>
        public static List<(double X, double Y)>? Arrange(IList<TextBounds> texts, IList<IReadOnlyList<(double X, double Y)>> obstacles,
            double poleX, double poleY, double angle, double length, double textHeight, int upSide = 1)
        {
            int n = texts.Count;
            var none = Enumerable.Repeat((0.0, 0.0), n).ToList();
            if (n == 0 || !obstacles.Any(o => texts.Any(t => Overlaps(t, o)))) return none;

            double ax = Math.Cos(angle), ay = Math.Sin(angle); // Ao longo da seta
            double cx = -ay, cy = ax;                            // Atravessado (para o lado esquerdo da seta)

            // Afastamento atual de cada texto da linha da seta (com sinal: lado) e metade da largura dele nessa direção
            double[] offset = texts.Select(t => (t.CenterX - poleX) * cx + (t.CenterY - poleY) * cy).ToArray();
            double[] half = texts.Select(t => (t.MaxX - t.MinX) / 2 * Math.Abs(cx) + (t.MaxY - t.MinY) / 2 * Math.Abs(cy)).ToArray();
            double clear = Math.Max(textHeight * 0.25, Enumerable.Range(0, n).Min(i => Math.Abs(offset[i]) - half[i]));
            double gap = textHeight * 0.3;

            // Afastamentos de destino de cada arrumação, com a penalidade dela (trocar de lado inverte valor e ângulo)
            var layouts = new List<(double[] Target, double Penalty)> { (offset, 0), (offset.Select(o => -o).ToArray(), 1000 * textHeight) };
            foreach (int side in new[] { 1, -1 })
            {
                // Empilhados do mesmo lado, a partir da seta: para cima, o de baixo vai primeiro (perto da seta) e o valor
                // fica em cima; para baixo, o valor vai primeiro, logo abaixo da seta
                var stacked = new double[n];
                double edge = clear;
                IEnumerable<int> outward = side == upSide ? Enumerable.Range(0, n).Reverse() : Enumerable.Range(0, n);
                foreach (int i in outward)
                {
                    stacked[i] = side * (edge + half[i]);
                    edge += 2 * half[i] + gap;
                }
                layouts.Add((stacked, 0));
            }

            var candidates = new List<(List<(double X, double Y)> Moves, double Penalty)>();
            foreach (var (target, penalty) in layouts)
            {
                foreach (double push in new[] { 0, 1.0, 2.0 })
                {
                    foreach (double slide in new[] { 0, 0.35, 0.7 })
                    {
                        candidates.Add((Enumerable.Range(0, n).Select(i =>
                        {
                            double across = target[i] - offset[i] + Math.Sign(target[i]) * push * textHeight;
                            double along = slide * length;
                            return (across * cx + along * ax, across * cy + along * ay);
                        }).ToList(), penalty));
                    }
                }
            }

            // Do menor deslocamento para o maior: o primeiro sem encostar em nada
            foreach (var moves in candidates.OrderBy(c => c.Penalty + c.Moves.Sum(v => Math.Sqrt(v.X * v.X + v.Y * v.Y))).Select(c => c.Moves))
            {
                bool free = true;
                for (int i = 0; i < n && free; i++)
                {
                    TextBounds moved = texts[i].Moved(moves[i].X, moves[i].Y);
                    free = !obstacles.Any(o => Overlaps(moved, o));
                    // Os dois textos da seta também não podem encostar um no outro
                    for (int j = 0; j < i && free; j++) free = !moved.Overlaps(texts[j].Moved(moves[j].X, moves[j].Y));
                }
                if (free) return moves;
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
