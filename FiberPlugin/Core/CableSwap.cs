using System.Text.RegularExpressions;

namespace FiberPlugin.Core
{
    /// <summary>Texto da layer do cabo que vai junto quando o tipo do cabo é trocado.</summary>
    public enum CableTextKind
    {
        None,   // Outro texto: fica como está
        Name,   // Nome do cabo ("ASU-80 06F.O"): troca pelo nome novo
        Span    // Metragem do vão ("32.8m"): só muda de layer
    }

    /// <summary>
    /// Regras da troca do tipo de um cabo já lançado (Trocar Cabo), sem depender da API do AutoCAD: que textos da layer do
    /// cabo são dele e a distância de um ponto ao percurso. Quem mexe no desenho é CableDrawing.ChangeType.
    /// </summary>
    public static class CableSwap
    {
        private static readonly Regex SpanPattern = new Regex(@"^\d+(?:[.,]\d+)?\s*m$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static CableTextKind Classify(string text, string cableName)
        {
            string t = text.Trim();
            if (t.Equals(cableName.Trim(), StringComparison.OrdinalIgnoreCase)) return CableTextKind.Name;
            return SpanPattern.IsMatch(t) ? CableTextKind.Span : CableTextKind.None;
        }

        /// <summary>Distância do ponto ao percurso (trechos retos entre os vértices).</summary>
        public static double DistanceToPath(IReadOnlyList<(double X, double Y)> path, (double X, double Y) p)
        {
            if (path.Count == 0) return double.MaxValue;
            if (path.Count == 1) return PlanarMath.Distance(path[0], p);
            double best = double.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var (ax, ay) = path[i];
                var (bx, by) = path[i + 1];
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - ax) * dx + (p.Y - ay) * dy) / len2));
                best = Math.Min(best, PlanarMath.Distance((ax + t * dx, ay + t * dy), p));
            }
            return best;
        }

        /// <summary>
        /// O texto é deste cabo? Perto dele (até <paramref name="reach"/>, que cobre o texto movido um pouco) e mais perto
        /// dele que de qualquer outro cabo (dois cabos do mesmo tipo lado a lado não levam os textos um do outro).
        /// </summary>
        public static bool Belongs((double X, double Y) text, IReadOnlyList<(double X, double Y)> cable,
            IEnumerable<IReadOnlyList<(double X, double Y)>> others, double reach)
        {
            double mine = DistanceToPath(cable, text);
            return mine <= reach && others.All(o => DistanceToPath(o, text) >= mine);
        }
    }
}
