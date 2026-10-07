using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Poste ou CTO/CEO que tem texto de identificação: handle, número escrito (P-12, CTO-03), extensão do desenho e o
    /// ponto em que o plugin põe o texto dele (Default).
    /// </summary>
    public sealed class LabelOwner
    {
        public string Handle { get; set; } = "";
        public bool IsBox { get; set; }
        public string Name { get; set; } = "";
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public Point3d Default { get; set; }

        /// <summary>Distância do ponto até o desenho do bloco (zero dentro dele).</summary>
        public double Distance(Point3d p)
        {
            double dx = Math.Max(0, Math.Max(MinX - p.X, p.X - MaxX));
            double dy = Math.Max(0, Math.Max(MinY - p.Y, p.Y - MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Texto de identificação: dono gravado nele ("" = nenhum), layer (CTO/CEO ou poste), primeira linha, posição e se
    /// tem cara de texto do plugin (coordenadas no do poste, CTO-/CEO- no da caixa).
    /// </summary>
    public sealed class LabelText
    {
        public ObjectId Id { get; set; }
        public string Owner { get; set; } = "";
        public bool IsBox { get; set; }
        public string FirstLine { get; set; } = "";
        public Point3d At { get; set; }
        public bool LooksLikeLabel { get; set; }
    }

    /// <summary>
    /// Qual texto é de qual bloco. Cada texto guarda o handle do bloco dono. Desde a 1.9.47 ele é gravado como handle do
    /// AutoCAD, que o COPY e o copiar e colar acompanham; nos textos de antes era texto puro, e a cópia continuava
    /// apontando para o bloco original (ou para um handle que nem existe no desenho novo). Sem esta conferência o plugin
    /// não achava o texto e criava outro por cima.
    /// </summary>
    public static class LabelMatching
    {
        /// <summary>
        /// 1. O handle gravado vale quando o bloco existe e o texto está a até <paramref name="wideReach"/> dele, ou o bloco
        ///    é o mais perto do texto (texto movido continua dele); entre vários textos que apontam para o mesmo bloco, fica
        ///    com ele o mais perto, e os outros são cópias.
        /// 2. Bloco sem texto adota o texto sem dono mais perto: até <paramref name="reach"/>, ou até
        ///    <paramref name="wideReach"/> se o número escrito for o dele.
        /// 3. Bloco cujo texto está exatamente onde o plugin o põe e que tem perto (até <paramref name="wideReach"/>) um
        ///    texto sem dono com cara de texto do plugin fica com esse outro: é o texto que o usuário moveu, num desenho
        ///    copiado em que o plugin tinha criado um texto novo no lugar padrão. O do lugar padrão sai.
        /// 4. Sobram os repetidos: textos sem dono exatamente em cima de um que ficou com algum bloco (e os do passo 3).
        /// </summary>
        /// <returns>Índices (em <paramref name="labels"/>) do texto de cada bloco, pelo handle, e dos textos a apagar.</returns>
        public static (Dictionary<string, List<int>> ByOwner, List<int> Duplicates) Resolve(IList<LabelOwner> owners, IList<LabelText> labels,
            double reach, double wideReach)
        {
            var byHandle = new Dictionary<string, LabelOwner>(StringComparer.OrdinalIgnoreCase);
            foreach (LabelOwner owner in owners)
            {
                if (!byHandle.ContainsKey(owner.Handle)) byHandle[owner.Handle] = owner;
            }

            // Bloco do mesmo tipo mais perto de cada texto
            var nearestCache = new Dictionary<int, LabelOwner?>();
            LabelOwner? NearestOf(int i)
            {
                if (!nearestCache.TryGetValue(i, out LabelOwner? nearest))
                {
                    nearest = owners.Where(o => o.IsBox == labels[i].IsBox).OrderBy(o => o.Distance(labels[i].At)).FirstOrDefault();
                    nearestCache[i] = nearest;
                }
                return nearest;
            }

            // 1. Handle gravado: vale se o texto está a até wideReach do bloco ou se o bloco é o mais perto dele (texto
            //    movido). Longe e junto de outro bloco é handle de outro desenho que por acaso existe neste (colado)
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var orphans = new List<int>();
            foreach (var group in Enumerable.Range(0, labels.Count).GroupBy(i => labels[i].Owner, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0 || !byHandle.TryGetValue(group.Key, out LabelOwner? owner))
                {
                    orphans.AddRange(group);
                    continue;
                }
                List<int> sorted = group.OrderBy(i => owner.Distance(labels[i].At)).ToList();
                int first = sorted[0];
                if (owner.Distance(labels[first].At) <= wideReach || NearestOf(first) == owner)
                {
                    result[owner.Handle] = first;
                    orphans.AddRange(sorted.Skip(1));
                }
                else
                {
                    orphans.AddRange(sorted);
                }
            }
            var nearestOwner = orphans.ToDictionary(i => i, NearestOf);

            // 2. Bloco sem texto: o texto sem dono perto dele. Até reach, qualquer um; até wideReach, o de mesmo número ou
            //    o que tem cara de texto do plugin e tem este bloco como o mais perto (texto movido). O de mesmo número primeiro
            var adopted = new HashSet<int>();
            var candidates = new List<(int Label, LabelOwner Owner, double Distance, bool SameName)>();
            foreach (int i in orphans)
            {
                foreach (LabelOwner owner in owners)
                {
                    if (owner.IsBox != labels[i].IsBox || result.ContainsKey(owner.Handle)) continue;
                    double d = owner.Distance(labels[i].At);
                    bool sameName = string.Equals(labels[i].FirstLine, owner.Name, StringComparison.OrdinalIgnoreCase);
                    bool moved = labels[i].LooksLikeLabel && nearestOwner[i] == owner;
                    if (d <= reach || ((sameName || moved) && d <= wideReach)) candidates.Add((i, owner, d, sameName));
                }
            }
            foreach (var c in candidates.OrderBy(c => c.SameName ? 0 : 1).ThenBy(c => c.Distance))
            {
                if (adopted.Contains(c.Label) || result.ContainsKey(c.Owner.Handle)) continue;
                result[c.Owner.Handle] = c.Label;
                adopted.Add(c.Label);
            }

            // 3. Texto movido pelo usuário contra o texto que o plugin criou no lugar padrão
            var replaced = new List<int>();
            var swaps = new List<(int Label, LabelOwner Owner, double Distance)>();
            foreach (int i in orphans.Where(i => !adopted.Contains(i) && labels[i].LooksLikeLabel))
            {
                LabelOwner? nearest = nearestOwner[i];
                if (nearest == null || nearest.Distance(labels[i].At) > wideReach || !result.TryGetValue(nearest.Handle, out int current)) continue;
                if (labels[current].At.DistanceTo(nearest.Default) > 1e-3) continue; // O texto atual foi colocado ou movido pelo usuário
                if (labels[current].At.DistanceTo(labels[i].At) < 1e-3) continue;      // Exatamente em cima: é repetido (passo 4)
                swaps.Add((i, nearest, nearest.Distance(labels[i].At)));
            }
            var swapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in swaps.OrderBy(s => s.Distance))
            {
                if (!swapped.Add(s.Owner.Handle)) continue;
                replaced.Add(result[s.Owner.Handle]);
                result[s.Owner.Handle] = s.Label;
                adopted.Add(s.Label);
            }

            // 4. Repetidos: sem dono e exatamente em cima de um texto que ficou com um bloco
            List<Point3d> kept = result.Values.Select(i => labels[i].At).ToList();
            List<int> duplicates = orphans
                .Where(i => !adopted.Contains(i) && kept.Any(k => k.DistanceTo(labels[i].At) < 1e-3))
                .Concat(replaced)
                .Distinct()
                .ToList();
            return (result.ToDictionary(p => p.Key, p => new List<int> { p.Value }, StringComparer.OrdinalIgnoreCase), duplicates);
        }
    }
}
