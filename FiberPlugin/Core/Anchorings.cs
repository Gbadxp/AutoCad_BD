namespace FiberPlugin.Core
{
    /// <summary>Uma amarração a inserir: posição do ponto base do bloco e rotação (radianos).</summary>
    public readonly struct AnchoringPlacement
    {
        public AnchoringPlacement(double x, double y, double rotation)
        {
            X = x;
            Y = y;
            Rotation = rotation;
        }

        public double X { get; }
        public double Y { get; }
        public double Rotation { get; }
    }

    /// <summary>
    /// Onde vão as amarrações de um poste: uma em cada direção de vão que sai dele. O cabo que passa pelo poste dá
    /// duas direções (uma de cada lado), o que termina nele (fim de rede) dá uma só, e numa derivação cada linha dá a
    /// sua. Direções quase iguais (cabos lado a lado no mesmo vão) contam uma vez. Cada amarração fica sobre a linha
    /// do cabo, com a parte redonda logo depois da borda do poste e a parte aberta apontando para o cabo, para fora
    /// do poste (o bloco AMARRAÇÃO é desenhado com a parte redonda em volta do ponto base e a aberta para +X).
    /// Coordenadas em metros.
    /// </summary>
    public static class Anchorings
    {
        private const double SameDirectionDeg = 10;

        /// <param name="cables">Vértices de cada cabo.</param>
        /// <param name="pole">Ponto de inserção do poste.</param>
        /// <param name="poleBox">Extensão do símbolo do poste (mín. e máx.), para a amarração ficar do lado de fora.</param>
        /// <param name="tolerance">Distância máxima entre o vértice do cabo e o poste (a mesma do cálculo de esforço).</param>
        /// <param name="reach">Quanto a parte redonda avança do ponto base para trás, na direção do poste (já na escala do bloco).</param>
        /// <param name="gap">Folga entre a borda do poste e a amarração.</param>
        public static List<AnchoringPlacement> Place(IEnumerable<IList<(double X, double Y)>> cables, (double X, double Y) pole,
            ((double X, double Y) Min, (double X, double Y) Max) poleBox, double tolerance, double reach, double gap)
        {
            var spans = new List<((double X, double Y) From, double Angle)>();
            foreach (IList<(double X, double Y)> cable in cables)
            {
                // Como no cálculo de esforço: de cada cabo, só o vértice mais próximo do poste
                int best = -1;
                double bestDistance = tolerance;
                for (int i = 0; i < cable.Count; i++)
                {
                    double d = Distance(cable[i], pole);
                    if (d <= bestDistance) { bestDistance = d; best = i; }
                }
                if (best < 0) continue;

                foreach (int neighbor in new[] { best - 1, best + 1 })
                {
                    if (neighbor < 0 || neighbor >= cable.Count || Distance(cable[neighbor], cable[best]) < 1e-6) continue;
                    double angle = Math.Atan2(cable[neighbor].Y - cable[best].Y, cable[neighbor].X - cable[best].X);
                    if (spans.Any(s => AngleBetween(s.Angle, angle) < SameDirectionDeg * Math.PI / 180)) continue;
                    spans.Add((cable[best], angle));
                }
            }

            return spans.Select(s =>
            {
                double dx = Math.Cos(s.Angle), dy = Math.Sin(s.Angle);
                double clear = ExitDistance(s.From, dx, dy, poleBox) + gap + reach;
                // Parte aberta (+X do bloco) apontando para o cabo: rotação = direção do vão
                return new AnchoringPlacement(s.From.X + dx * clear, s.From.Y + dy * clear, Normalize(s.Angle));
            }).ToList();
        }

        /// <summary>Distância, na direção (dx, dy), do ponto até sair do retângulo do poste (0 se já estiver fora).</summary>
        public static double ExitDistance((double X, double Y) from, double dx, double dy, ((double X, double Y) Min, (double X, double Y) Max) box)
        {
            if (from.X < box.Min.X || from.X > box.Max.X || from.Y < box.Min.Y || from.Y > box.Max.Y) return 0;
            double tx = Math.Abs(dx) < 1e-12 ? double.MaxValue : (dx > 0 ? box.Max.X - from.X : from.X - box.Min.X) / Math.Abs(dx);
            double ty = Math.Abs(dy) < 1e-12 ? double.MaxValue : (dy > 0 ? box.Max.Y - from.Y : from.Y - box.Min.Y) / Math.Abs(dy);
            return Math.Min(tx, ty);
        }

        private static double AngleBetween(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

        private static double Normalize(double angle)
        {
            angle %= 2 * Math.PI;
            return angle < 0 ? angle + 2 * Math.PI : angle;
        }

        private static double Distance((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
