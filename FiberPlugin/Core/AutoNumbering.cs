using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Ordem da numeração automática dos postes (FIBRA_NUMERAR_AUTO): a partir do primeiro poste, segue os cabos.
    /// Em cada poste continua primeiro pelo mesmo cabo (e, entre vários, pelo vão mais reto); as derivações entram quando
    /// o ramo acaba, como na numeração feita à mão. Postes que os cabos não alcançam entram depois, a partir do mais
    /// próximo do último numerado.
    /// </summary>
    public static class AutoNumbering
    {
        /// <param name="poles">Posição dos postes a numerar.</param>
        /// <param name="start">Índice do primeiro poste.</param>
        /// <param name="cables">Vértices de cada cabo; cada vértice vale para o poste mais próximo a até <paramref name="linkRadius"/>.</param>
        /// <param name="passThrough">Postes que ficam fora da numeração (seleção parcial): o vértice que é deles não liga a
        /// nenhum poste numerado, e o cabo passa por eles até o próximo.</param>
        /// <returns>Índices dos postes na ordem nova e quantos deles nenhum cabo toca (numerados pela proximidade).</returns>
        public static (List<int> Order, int WithoutCable) Order(IList<Point3d> poles, int start, IEnumerable<IList<Point3d>> cables, double linkRadius,
            IList<Point3d>? passThrough = null)
        {
            int n = poles.Count;
            var order = new List<int>(n);
            if (n == 0) return (order, 0);

            // Vizinhos de cada poste pelos cabos, com os cabos que fazem a ligação
            var neighbors = Enumerable.Range(0, n).Select(_ => new Dictionary<int, HashSet<int>>()).ToArray();
            var touched = new bool[n];
            int cableIndex = 0;
            foreach (IList<Point3d> cable in cables)
            {
                int previous = -1;
                foreach (Point3d vertex in cable)
                {
                    int pole = Nearest(poles, vertex, linkRadius);
                    if (pole >= 0 && passThrough != null && passThrough.Any(p => p.DistanceTo(vertex) < poles[pole].DistanceTo(vertex))) continue;
                    if (pole >= 0) touched[pole] = true;
                    if (pole < 0 || pole == previous) continue;
                    if (previous >= 0)
                    {
                        Link(neighbors, previous, pole, cableIndex);
                        Link(neighbors, pole, previous, cableIndex);
                    }
                    previous = pole;
                }
                cableIndex++;
            }

            var visited = new bool[n];
            int current = Math.Max(0, Math.Min(n - 1, start));
            while (true)
            {
                Walk(poles, neighbors, visited, order, current);
                if (order.Count == n) break;

                // Próximo trecho: o poste ainda sem número mais perto do último numerado
                Point3d last = poles[order[order.Count - 1]];
                current = Enumerable.Range(0, n).Where(i => !visited[i]).OrderBy(i => poles[i].DistanceTo(last)).First();
            }
            return (order, touched.Count(t => !t));
        }

        private static void Link(Dictionary<int, HashSet<int>>[] neighbors, int from, int to, int cable)
        {
            if (!neighbors[from].TryGetValue(to, out HashSet<int>? set)) neighbors[from][to] = set = new HashSet<int>();
            set.Add(cable);
        }

        /// <summary>Percorre os cabos a partir de <paramref name="from"/> (em profundidade, sem recursão: trechos longos não estouram a pilha).</summary>
        private static void Walk(IList<Point3d> poles, Dictionary<int, HashSet<int>>[] neighbors, bool[] visited, List<int> order, int from)
        {
            visited[from] = true;
            order.Add(from);
            var stack = new Stack<(int Pole, Queue<int> Next)>();
            stack.Push((from, Sorted(poles, neighbors, visited, from, -1)));

            while (stack.Count > 0)
            {
                var (pole, next) = stack.Peek();
                int go = -1;
                while (next.Count > 0)
                {
                    int candidate = next.Dequeue();
                    if (!visited[candidate]) { go = candidate; break; }
                }
                if (go < 0)
                {
                    stack.Pop();
                    continue;
                }

                visited[go] = true;
                order.Add(go);
                stack.Push((go, Sorted(poles, neighbors, visited, go, pole)));
            }
        }

        /// <summary>
        /// Vizinhos ainda sem número, na ordem de visita: primeiro os do mesmo cabo por onde se chegou, depois o vão mais
        /// reto. No primeiro poste (sem vão de chegada), o mais perto primeiro.
        /// </summary>
        private static Queue<int> Sorted(IList<Point3d> poles, Dictionary<int, HashSet<int>>[] neighbors, bool[] visited, int pole, int cameFrom)
        {
            IEnumerable<int> candidates = neighbors[pole].Keys.Where(i => !visited[i]);
            if (cameFrom < 0) return new Queue<int>(candidates.OrderBy(i => poles[i].DistanceTo(poles[pole])));

            HashSet<int> arrivedBy = neighbors[pole][cameFrom];
            Vector3d incoming = poles[pole] - poles[cameFrom];
            return new Queue<int>(candidates
                .OrderBy(i => neighbors[pole][i].Overlaps(arrivedBy) ? 0 : 1)
                .ThenBy(i => incoming.Length < 1e-9 ? 0 : incoming.GetAngleTo(poles[i] - poles[pole])));
        }

        private static int Nearest(IList<Point3d> poles, Point3d point, double radius)
        {
            int best = -1;
            double bestDistance = radius;
            for (int i = 0; i < poles.Count; i++)
            {
                double d = poles[i].DistanceTo(point);
                if (d <= bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }
    }
}
