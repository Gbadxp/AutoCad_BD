using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Poste ou CTO/CEO que tem texto de identificação: handle, número escrito (P-12, CTO-03) e extensão do desenho.</summary>
    public sealed class LabelOwner
    {
        public string Handle { get; set; } = "";
        public bool IsBox { get; set; }
        public string Name { get; set; } = "";
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        /// <summary>Distância do ponto até o desenho do bloco (zero dentro dele).</summary>
        public double Distance(Point3d p)
        {
            double dx = Math.Max(0, Math.Max(MinX - p.X, p.X - MaxX));
            double dy = Math.Max(0, Math.Max(MinY - p.Y, p.Y - MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>Texto de identificação: dono gravado nele ("" = nenhum), layer (CTO/CEO ou poste), primeira linha e posição.</summary>
    public sealed class LabelText
    {
        public ObjectId Id { get; set; }
        public string Owner { get; set; } = "";
        public bool IsBox { get; set; }
        public string FirstLine { get; set; } = "";
        public Point3d At { get; set; }
    }

    /// <summary>
    /// Qual texto é de qual bloco. Cada texto guarda o handle do bloco dono, mas o handle muda quando o desenho é copiado
    /// (COPY, copiar e colar de outro DWG): a cópia do texto continua apontando para o bloco original, ou para um handle
    /// que não existe (ou é de outro bloco) no desenho novo. Sem isso o plugin não achava o texto e criava outro por cima.
    /// </summary>
    public static class LabelMatching
    {
        /// <summary>
        /// O handle gravado vale quando o bloco existe e o texto está junto dele (o mais perto, entre os que apontam para
        /// ele, e não claramente junto de outro bloco). Os outros textos vão para o bloco ainda sem texto mais perto, a até
        /// <paramref name="reach"/>, preferindo o que tem o mesmo número escrito na primeira linha. Sobram os textos
        /// repetidos: os que estão exatamente em cima de um texto que ficou com algum bloco.
        /// </summary>
        /// <returns>Índices (em <paramref name="labels"/>) do texto de cada bloco, pelo handle, e dos textos repetidos.</returns>
        public static (Dictionary<string, List<int>> ByOwner, List<int> Duplicates) Resolve(IList<LabelOwner> owners, IList<LabelText> labels,
            double reach, double tolerance)
        {
            var byHandle = new Dictionary<string, LabelOwner>(StringComparer.OrdinalIgnoreCase);
            foreach (LabelOwner owner in owners)
            {
                if (!byHandle.ContainsKey(owner.Handle)) byHandle[owner.Handle] = owner;
            }

            var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var orphans = new List<int>();
            foreach (var group in Enumerable.Range(0, labels.Count).GroupBy(i => labels[i].Owner, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0 || !byHandle.TryGetValue(group.Key, out LabelOwner? owner))
                {
                    orphans.AddRange(group);
                    continue;
                }

                // Do dono fica o texto mais perto dele; os demais que apontam para ele são cópias
                List<int> sorted = group.OrderBy(i => owner.Distance(labels[i].At)).ToList();
                Point3d at = labels[sorted[0]].At;
                double own = owner.Distance(at);
                double other = owners.Where(o => o != owner && o.IsBox == owner.IsBox).Select(o => o.Distance(at)).DefaultIfEmpty(double.MaxValue).Min();
                if (own <= other * 1.5 + tolerance)
                {
                    result[owner.Handle] = new List<int> { sorted[0] };
                    orphans.AddRange(sorted.Skip(1));
                }
                else
                {
                    orphans.AddRange(sorted); // Aponta para um bloco longe e está junto de outro: veio de cópia
                }
            }

            // Textos sem dono certo: para o bloco ainda sem texto mais perto (mesmo número escrito primeiro)
            var candidates = new List<(int Label, LabelOwner Owner, double Distance, bool SameName)>();
            foreach (int i in orphans)
            {
                foreach (LabelOwner owner in owners)
                {
                    if (owner.IsBox != labels[i].IsBox || result.ContainsKey(owner.Handle)) continue;
                    double d = owner.Distance(labels[i].At);
                    if (d <= reach) candidates.Add((i, owner, d, string.Equals(labels[i].FirstLine, owner.Name, StringComparison.OrdinalIgnoreCase)));
                }
            }
            var adopted = new HashSet<int>();
            foreach (var c in candidates.OrderBy(c => c.SameName ? 0 : 1).ThenBy(c => c.Distance))
            {
                if (adopted.Contains(c.Label) || result.ContainsKey(c.Owner.Handle)) continue;
                result[c.Owner.Handle] = new List<int> { c.Label };
                adopted.Add(c.Label);
            }

            // Repetidos: sem dono e exatamente em cima de um texto que ficou com um bloco
            List<Point3d> kept = result.Values.SelectMany(v => v).Select(i => labels[i].At).ToList();
            List<int> duplicates = orphans
                .Where(i => !adopted.Contains(i) && kept.Any(k => k.DistanceTo(labels[i].At) < 1e-3))
                .ToList();
            return (result, duplicates);
        }
    }
}
