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

        /// <summary>Vínculo gravado como handle do AutoCAD (1.9.47 em diante), que o COPY e o colar mantêm certo.</summary>
        public bool LinkIsHandle { get; set; }
    }

    /// <summary>
    /// Qual texto é de qual bloco. Cada texto guarda o handle do bloco dono como texto, que a cópia não acompanha: em
    /// desenho copiado (COPY, copiar e colar de outro DWG) a cópia continua apontando para o bloco original, ou para um
    /// handle que nem existe no desenho novo. Sem esta conferência o plugin não achava o texto e criava outro por cima.
    /// (Os textos gravados pelas 1.9.47 e 1.9.48 têm o handle do AutoCAD, que a cópia acompanha.)
    /// </summary>
    public static class LabelMatching
    {
        /// <summary>
        /// 1. Vínculo gravado como handle do AutoCAD (1.9.47 e 1.9.48) vale sempre, esteja o texto onde estiver (vários
        ///    textos assim do mesmo bloco, ex.: uma cópia num detalhe, todos acompanham). Vínculo em texto vale se o texto
        ///    está a até <paramref name="wideReach"/> do bloco, o bloco é o mais perto dele ou o texto tem o número do bloco
        ///    escrito (texto movido para longe); entre vários que apontam para o mesmo bloco, fica com ele o mais perto, e os
        ///    outros são cópias.
        /// 2. Bloco sem texto adota o texto sem dono perto dele: até <paramref name="reach"/>, qualquer um; até
        ///    <paramref name="wideReach"/>, o que tem o número dele escrito ou o texto do plugin que é "dele" (veja IntendedOf).
        /// 3. Bloco cujo único texto está exatamente onde o plugin o põe e que tem perto um texto sem dono do plugin que é
        ///    "dele" fica com esse outro: é o texto que o usuário moveu, num desenho copiado em que o plugin tinha criado um
        ///    texto novo no lugar padrão. O do lugar padrão sai.
        /// 4. Sobram os repetidos: textos sem dono exatamente em cima de um que ficou com algum bloco (e os do passo 3).
        /// </summary>
        /// <returns>Índices (em <paramref name="labels"/>) dos textos de cada bloco, pelo handle, e dos textos a apagar.</returns>
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

            // Bloco "dele" para um texto sem dono: o de mesmo número escrito a até wideReach (o texto pode ter sido arrastado
            // para perto do vizinho) ou, sem ele, o mais perto
            LabelOwner? IntendedOf(int i) =>
                owners.Where(o => o.IsBox == labels[i].IsBox && string.Equals(o.Name, labels[i].FirstLine, StringComparison.OrdinalIgnoreCase) &&
                                  o.Distance(labels[i].At) <= wideReach)
                      .OrderBy(o => o.Distance(labels[i].At)).FirstOrDefault()
                ?? NearestOf(i);

            // 1. Vínculo gravado
            var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var orphans = new List<int>();
            foreach (var group in Enumerable.Range(0, labels.Count).GroupBy(i => labels[i].Owner, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0 || !byHandle.TryGetValue(group.Key, out LabelOwner? owner))
                {
                    orphans.AddRange(group);
                    continue;
                }

                List<int> handleLinks = group.Where(i => labels[i].LinkIsHandle).ToList();
                if (handleLinks.Count > 0)
                {
                    result[owner.Handle] = handleLinks;
                    orphans.AddRange(group.Where(i => !labels[i].LinkIsHandle));
                    continue;
                }

                // Vínculo em texto: perto do bloco, o bloco é o mais perto do texto, ou o texto tem o número dele escrito e não
                // está absurdamente longe (texto movido para longe, até 5 vezes wideReach). Longe, junto de outro bloco e com
                // outro número (ou muito longe) é handle de outro desenho que por acaso existe neste (colado)
                List<int> sorted = group.OrderBy(i => owner.Distance(labels[i].At)).ToList();
                int first = sorted[0];
                double distance = owner.Distance(labels[first].At);
                if (distance <= wideReach || NearestOf(first) == owner ||
                    (distance <= 5 * wideReach && string.Equals(labels[first].FirstLine, owner.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    result[owner.Handle] = new List<int> { first };
                    orphans.AddRange(sorted.Skip(1));
                }
                else
                {
                    orphans.AddRange(sorted);
                }
            }
            var intended = orphans.ToDictionary(i => i, IntendedOf);

            // 2. Bloco sem texto: o texto sem dono perto dele (o de mesmo número primeiro)
            var adopted = new HashSet<int>();
            var candidates = new List<(int Label, LabelOwner Owner, double Distance, bool SameName)>();
            foreach (int i in orphans)
            {
                foreach (LabelOwner owner in owners)
                {
                    if (owner.IsBox != labels[i].IsBox || result.ContainsKey(owner.Handle)) continue;
                    double d = owner.Distance(labels[i].At);
                    bool sameName = string.Equals(labels[i].FirstLine, owner.Name, StringComparison.OrdinalIgnoreCase);
                    bool moved = labels[i].LooksLikeLabel && intended[i] == owner;
                    if (d <= reach || ((sameName || moved) && d <= wideReach)) candidates.Add((i, owner, d, sameName));
                }
            }
            foreach (var c in candidates.OrderBy(c => c.SameName ? 0 : 1).ThenBy(c => c.Distance))
            {
                if (adopted.Contains(c.Label) || result.ContainsKey(c.Owner.Handle)) continue;
                result[c.Owner.Handle] = new List<int> { c.Label };
                adopted.Add(c.Label);
            }

            // 3. Texto movido pelo usuário contra o texto que o plugin criou no lugar padrão
            var replaced = new List<int>();
            var swaps = new List<(int Label, LabelOwner Owner, double Distance)>();
            foreach (int i in orphans.Where(i => !adopted.Contains(i) && labels[i].LooksLikeLabel))
            {
                LabelOwner? target = intended[i];
                if (target == null || target.Distance(labels[i].At) > wideReach || !result.TryGetValue(target.Handle, out List<int>? current) ||
                    current.Count != 1) continue;
                if (labels[current[0]].At.DistanceTo(target.Default) > 1e-3) continue; // O texto atual foi colocado ou movido pelo usuário
                if (labels[current[0]].At.DistanceTo(labels[i].At) < 1e-3) continue;   // Exatamente em cima: é repetido (passo 4)
                swaps.Add((i, target, target.Distance(labels[i].At)));
            }
            var swapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in swaps.OrderBy(s => string.Equals(labels[s.Label].FirstLine, s.Owner.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                   .ThenBy(s => s.Distance))
            {
                if (!swapped.Add(s.Owner.Handle)) continue;
                replaced.Add(result[s.Owner.Handle][0]);
                result[s.Owner.Handle] = new List<int> { s.Label };
                adopted.Add(s.Label);
            }

            // 4. Repetidos: sem dono e exatamente em cima de um texto que ficou com um bloco
            List<Point3d> kept = result.Values.SelectMany(v => v).Select(i => labels[i].At).ToList();
            List<int> duplicates = orphans
                .Where(i => !adopted.Contains(i) && kept.Any(k => k.DistanceTo(labels[i].At) < 1e-3))
                .Concat(replaced)
                .Distinct()
                .ToList();
            return (result, duplicates);
        }
    }
}
