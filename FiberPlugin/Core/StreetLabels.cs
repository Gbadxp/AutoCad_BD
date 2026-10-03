namespace FiberPlugin.Core
{
    /// <summary>Um nome de rua a escrever: ponto no eixo da rua e ângulo do texto (radianos, sempre de pé).</summary>
    public readonly struct StreetLabel
    {
        public StreetLabel(string text, double x, double y, double angle)
        {
            Text = text;
            X = x;
            Y = y;
            Angle = angle;
        }

        public string Text { get; }
        public double X { get; }
        public double Y { get; }
        public double Angle { get; }
    }

    /// <summary>
    /// Onde escrever o nome de cada rua (coordenadas em metros). Os trechos do OSM com o mesmo nome que se tocam
    /// viram uma linha só; cada linha recebe o nome no meio, e outro a cada <c>spacing</c> nas ruas longas, na
    /// direção da rua medida ao longo do comprimento do texto. Linhas mais curtas que o texto ficam sem nome, e o
    /// mesmo nome não se repete perto de outro (pistas duplas, rua cortada em vários trechos).
    /// </summary>
    public static class StreetLabels
    {
        private const double JoinTolerance = 0.5; // m entre pontas para dois trechos serem a mesma rua

        public static List<StreetLabel> Place(IEnumerable<(string Name, IList<(double X, double Y)> Line)> runs,
            double textHeight, double spacing)
        {
            var labels = new List<StreetLabel>();
            foreach (var street in runs.Where(r => r.Name.Trim().Length > 0 && r.Line.Count >= 2).GroupBy(r => r.Name.Trim()))
            {
                double textLength = TextLength(street.Key, textHeight);
                double minGap = Math.Max(textLength * 1.5, spacing / 2);
                var placed = new List<(double X, double Y)>();

                foreach (List<(double X, double Y)> chain in Chains(street.Select(r => r.Line)).OrderByDescending(Length))
                {
                    double length = Length(chain);
                    if (length < textLength * 1.2) continue;

                    int count = Math.Max(1, (int)Math.Round(length / spacing));
                    for (int i = 0; i < count; i++)
                    {
                        double s = Math.Max(textLength / 2, Math.Min(length - textLength / 2, (i + 0.5) * length / count));
                        var p = PointAt(chain, s);
                        if (placed.Any(q => PlanarMath.Distance(q, p) < minGap)) continue;

                        // Direção da corda que o texto ocupa: acompanha a rua mesmo em curva
                        var a = PointAt(chain, s - textLength / 2);
                        var b = PointAt(chain, s + textLength / 2);
                        labels.Add(new StreetLabel(street.Key, p.X, p.Y, PlanarMath.ReadableAngle(Math.Atan2(b.Y - a.Y, b.X - a.X))));
                        placed.Add(p);
                    }
                }
            }
            return labels;
        }

        /// <summary>Comprimento aproximado do texto na fonte padrão (largura média de 0,7 da altura por letra).</summary>
        public static double TextLength(string text, double height) => text.Length * height * 0.7;

        /// <summary>Junta os trechos que se tocam pelas pontas (virando os que estiverem ao contrário).</summary>
        private static List<List<(double X, double Y)>> Chains(IEnumerable<IList<(double X, double Y)>> lines)
        {
            var pending = lines.Select(l => l.ToList()).ToList();
            var chains = new List<List<(double X, double Y)>>();
            while (pending.Count > 0)
            {
                List<(double X, double Y)> chain = pending[0];
                pending.RemoveAt(0);

                for (bool grew = true; grew;)
                {
                    grew = false;
                    for (int i = 0; i < pending.Count && !grew; i++)
                    {
                        List<(double X, double Y)> other = pending[i];
                        var start = chain[0];
                        var end = chain[chain.Count - 1];
                        var otherStart = other[0];
                        var otherEnd = other[other.Count - 1];

                        if (PlanarMath.Distance(end, otherStart) < JoinTolerance) chain.AddRange(other.Skip(1));
                        else if (PlanarMath.Distance(end, otherEnd) < JoinTolerance) chain.AddRange(Enumerable.Reverse(other).Skip(1));
                        else if (PlanarMath.Distance(start, otherEnd) < JoinTolerance) chain.InsertRange(0, other.Take(other.Count - 1));
                        else if (PlanarMath.Distance(start, otherStart) < JoinTolerance) chain.InsertRange(0, Enumerable.Reverse(other).Take(other.Count - 1));
                        else continue;

                        pending.RemoveAt(i);
                        grew = true;
                    }
                }
                chains.Add(chain);
            }
            return chains;
        }

        private static double Length(List<(double X, double Y)> line)
        {
            double length = 0;
            for (int i = 0; i + 1 < line.Count; i++) length += PlanarMath.Distance(line[i], line[i + 1]);
            return length;
        }

        /// <summary>Ponto a <paramref name="s"/> metros do início da linha (limitado às pontas).</summary>
        private static (double X, double Y) PointAt(List<(double X, double Y)> line, double s)
        {
            if (s <= 0) return line[0];
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double segment = PlanarMath.Distance(line[i], line[i + 1]);
                if (s <= segment && segment > 0)
                {
                    double t = s / segment;
                    return (line[i].X + t * (line[i + 1].X - line[i].X), line[i].Y + t * (line[i + 1].Y - line[i].Y));
                }
                s -= segment;
            }
            return line[line.Count - 1];
        }
    }
}
