using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Ordenação de pontos para o roteamento automático e afastamento da rota em relação aos postes.</summary>
    public static class RouteOptimizer
    {
        private const int MaxStartsToTry = 200;

        /// <summary>Remove pontos repetidos (ex.: CTO inserida exatamente no centro do poste).</summary>
        public static List<Point3d> Dedupe(IEnumerable<Point3d> points, double tolerance)
        {
            var result = new List<Point3d>();
            foreach (Point3d p in points)
            {
                if (!result.Any(q => q.DistanceTo(p) <= tolerance)) result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Caminho aberto curto passando por todos os pontos: vizinho mais próximo testando vários
        /// pontos de partida (antes começava pelo primeiro da seleção, que é arbitrário) + melhoria 2-opt,
        /// que desfaz cruzamentos e zigue-zagues.
        /// </summary>
        public static List<Point3d> Order(IList<Point3d> points)
        {
            if (points.Count <= 2) return points.ToList();

            IEnumerable<int> starts = Enumerable.Range(0, points.Count);
            if (points.Count > MaxStartsToTry)
            {
                // Muitos pontos: testa só os mais afastados do centro (candidatos naturais a ponta de rota)
                var center = new Point3d(points.Average(p => p.X), points.Average(p => p.Y), 0);
                starts = starts.OrderByDescending(i => points[i].DistanceTo(center)).Take(10);
            }

            List<Point3d> best = starts
                .Select(s => NearestNeighbor(points, s))
                .OrderBy(PathLength)
                .First();

            TwoOpt(best);
            return best;
        }

        public static double PathLength(IList<Point3d> path)
        {
            double total = 0;
            for (int i = 0; i < path.Count - 1; i++) total += path[i].DistanceTo(path[i + 1]);
            return total;
        }

        /// <summary>
        /// Desloca cada vértice exatamente <paramref name="distance"/> do ponto original, na direção da
        /// bissetriz. Diferente do Offset do AutoCAD, os cantos não se afastam mais que a distância pedida,
        /// então o vértice continua dentro do raio de busca do poste nos cálculos de esforço.
        /// </summary>
        public static List<Point3d> OffsetPath(IList<Point3d> path, double distance)
        {
            var result = new List<Point3d>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                Vector2d? prev = i > 0 ? LeftNormal(path[i - 1], path[i]) : null;
                Vector2d? next = i < path.Count - 1 ? LeftNormal(path[i], path[i + 1]) : null;

                Vector2d dir = (prev ?? new Vector2d(0, 0)) + (next ?? new Vector2d(0, 0));
                if (dir.Length < 1e-6) dir = prev ?? next ?? new Vector2d(0, 1); // Retorno de 180°
                dir = dir.GetNormal();

                result.Add(new Point3d(path[i].X + dir.X * distance, path[i].Y + dir.Y * distance, 0));
            }
            return result;
        }

        private static Vector2d? LeftNormal(Point3d a, Point3d b)
        {
            var v = new Vector2d(b.X - a.X, b.Y - a.Y);
            if (v.Length < 1e-9) return null;
            v = v.GetNormal();
            return new Vector2d(-v.Y, v.X);
        }

        private static List<Point3d> NearestNeighbor(IList<Point3d> points, int start)
        {
            var visited = new bool[points.Count];
            var path = new List<Point3d>(points.Count) { points[start] };
            visited[start] = true;
            int current = start;

            for (int step = 1; step < points.Count; step++)
            {
                int nearest = -1;
                double nearestDist = double.MaxValue;
                for (int j = 0; j < points.Count; j++)
                {
                    if (visited[j]) continue;
                    double d = points[current].DistanceTo(points[j]);
                    if (d < nearestDist) { nearestDist = d; nearest = j; }
                }
                visited[nearest] = true;
                path.Add(points[nearest]);
                current = nearest;
            }
            return path;
        }

        private static void TwoOpt(List<Point3d> path)
        {
            int n = path.Count;
            if (n < 4) return;

            double D(int a, int b) => path[a].DistanceTo(path[b]);

            bool improved = true;
            for (int pass = 0; improved && pass < 1000; pass++)
            {
                improved = false;

                // Inverte o início do caminho: troca a aresta (k, k+1) por (0, k+1)
                for (int k = 1; k < n - 1; k++)
                {
                    if (D(0, k + 1) - D(k, k + 1) < -1e-9)
                    {
                        path.Reverse(0, k + 1);
                        improved = true;
                    }
                }

                // Inverte um trecho interno ou o final do caminho
                for (int i = 0; i < n - 2; i++)
                {
                    for (int k = i + 2; k < n; k++)
                    {
                        double delta = k == n - 1
                            ? D(i, k) - D(i, i + 1)
                            : D(i, k) + D(i + 1, k + 1) - D(i, i + 1) - D(k, k + 1);

                        if (delta < -1e-9)
                        {
                            path.Reverse(i + 1, k - i);
                            improved = true;
                        }
                    }
                }
            }
        }
    }
}
