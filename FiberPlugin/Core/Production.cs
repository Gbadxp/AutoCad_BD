using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FiberPlugin.Core
{
    /// <summary>
    /// O que um desenho tem num momento, para a produção do dia: metros de cada cabo, CTO, CEO, postes, amarrações
    /// (ancoragens), reservas técnicas (textos como "RESERVA TÉCNICA 25m") e metros de cordoalha. A diferença entre dois momentos é o que foi feito entre eles.
    /// </summary>
    public sealed class ProductionCount
    {
        /// <summary>Metros de cada tipo de cabo (nome curto, ex.: ASU-80 06F.O).</summary>
        public Dictionary<string, double> CableMeters { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public int Cto { get; set; }
        public int Ceo { get; set; }
        public int Poles { get; set; }

        /// <summary>Blocos de amarração (ancoragem do cabo no poste).</summary>
        public int Anchorings { get; set; }

        /// <summary>
        /// As amarrações foram contadas. Anotação das versões anteriores à 1.9.77 não tem: comparada com uma nova, a
        /// diferença de amarrações fica zero (senão as que já estavam no desenho pareceriam feitas no dia).
        /// </summary>
        public bool AnchoringsCounted { get; set; } = true;
        public int Reserves { get; set; }
        public double ReserveMeters { get; set; }
        public double StrandMeters { get; set; }

        public double CableTotal => CableMeters.Values.Sum();

        /// <summary>Nada mudou (diferenças de menos de 1 cm não contam).</summary>
        public bool IsZero => Cto == 0 && Ceo == 0 && Poles == 0 && Anchorings == 0 && Reserves == 0 && Math.Abs(ReserveMeters) < 0.01 &&
                              Math.Abs(StrandMeters) < 0.01 && CableMeters.Values.All(m => Math.Abs(m) < 0.01);

        /// <summary>O que este momento tem a mais que <paramref name="start"/> (negativo onde foi apagado).</summary>
        public ProductionCount Minus(ProductionCount start) => Combine(start, -1);

        public ProductionCount Plus(ProductionCount other) => Combine(other, 1);

        private ProductionCount Combine(ProductionCount other, int sign)
        {
            var result = new ProductionCount
            {
                Cto = Cto + sign * other.Cto,
                Ceo = Ceo + sign * other.Ceo,
                Poles = Poles + sign * other.Poles,
                Anchorings = AnchoringsCounted && other.AnchoringsCounted ? Anchorings + sign * other.Anchorings : sign > 0 ? Anchorings + other.Anchorings : 0,
                Reserves = Reserves + sign * other.Reserves,
                ReserveMeters = ReserveMeters + sign * other.ReserveMeters,
                StrandMeters = StrandMeters + sign * other.StrandMeters
            };
            foreach (string name in CableMeters.Keys.Union(other.CableMeters.Keys, StringComparer.OrdinalIgnoreCase))
            {
                double meters = (CableMeters.TryGetValue(name, out double a) ? a : 0) + sign * (other.CableMeters.TryGetValue(name, out double b) ? b : 0);
                if (Math.Abs(meters) >= 0.01) result.CableMeters[name] = meters;
            }
            return result;
        }

        // Reserva técnica escrita no desenho: "RESERVA TÉCNICA 25m", "Reserva tecnica de 30 m", "RESERVA 20 METROS"
        private static readonly Regex ReserveMetersPattern =
            new Regex(@"(\d+(?:[.,]\d+)?)\s*(?:m|mts?|metros?)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// O texto é de uma reserva técnica (tem "RESERVA")? <paramref name="meters"/> = a metragem escrita nele (0 se não
        /// tem).
        /// </summary>
        public static bool IsReserve(string text, out double meters)
        {
            meters = 0;
            if (text.IndexOf("RESERVA", StringComparison.OrdinalIgnoreCase) < 0) return false;
            Match m = ReserveMetersPattern.Match(text);
            if (m.Success) double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out meters);
            return true;
        }

        // Campos do arquivo de produção: "CTO=3", "Cabo ASU-80 06F.O=1234.56" (ponto decimal)
        private const string CablePrefix = "Cabo ";

        public IEnumerable<string> ToFields()
        {
            string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            yield return $"CTO={Cto}";
            yield return $"CEO={Ceo}";
            yield return $"Postes={Poles}";
            yield return $"Amarracoes={Anchorings}";
            yield return $"Reservas={Reserves}";
            yield return $"Reservas (m)={N(ReserveMeters)}";
            yield return $"Cordoalha (m)={N(StrandMeters)}";
            foreach (var cable in CableMeters.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                yield return $"{CablePrefix}{cable.Key}={N(cable.Value)}";
        }

        public static ProductionCount FromFields(IEnumerable<string> fields)
        {
            var count = new ProductionCount { AnchoringsCounted = false };
            foreach (string field in fields)
            {
                int eq = field.LastIndexOf('=');
                if (eq <= 0 || !double.TryParse(field.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                string key = field.Substring(0, eq);
                if (key.StartsWith(CablePrefix, StringComparison.Ordinal)) count.CableMeters[key.Substring(CablePrefix.Length)] = v;
                else if (key == "CTO") count.Cto = (int)v;
                else if (key == "CEO") count.Ceo = (int)v;
                else if (key == "Postes") count.Poles = (int)v;
                else if (key == "Amarracoes")
                {
                    count.Anchorings = (int)v;
                    count.AnchoringsCounted = true;
                }
                else if (key == "Reservas") count.Reserves = (int)v;
                else if (key == "Reservas (m)") count.ReserveMeters = v;
                else if (key == "Cordoalha (m)") count.StrandMeters = v;
            }
            return count;
        }
    }

    /// <summary>Uma anotação do arquivo de produção: o que o desenho tinha naquele momento.</summary>
    public sealed class ProductionEntry
    {
        public const string StartEvent = "inicio";   // Começo do dia (abertura, ou primeiro comando do dia)
        public const string SavedEvent = "salvo";

        public DateTime When { get; set; }
        public string Event { get; set; } = "";
        public string File { get; set; } = "";
        public ProductionCount Count { get; set; } = new ProductionCount();
    }

    /// <summary>
    /// Arquivo de produção: uma linha por anotação, um arquivo por mês (producao-2026-10.txt) na pasta Producao de
    /// Documentos\Fiber Plugin. Colunas separadas por tabulação: data e hora, evento, desenho e os números.
    /// </summary>
    public sealed class ProductionLog
    {
        public ProductionLog(string folder) => Folder = folder;

        public string Folder { get; }

        public static ProductionLog Default => new ProductionLog(Path.Combine(PluginPaths.UserRoot, "Producao"));

        public string FileFor(DateTime day) => Path.Combine(Folder, $"producao-{day:yyyy-MM}.txt");

        /// <summary>Anota o que o desenho tem agora. Sem conseguir gravar (pasta bloqueada), não anota.</summary>
        public void Append(DateTime when, string evt, string file, ProductionCount count)
        {
            string line = string.Join("\t", new[]
            {
                when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), evt, file
            }.Concat(count.ToFields())) + Environment.NewLine;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = FileFor(when);
                if (!System.IO.File.Exists(path))
                {
                    line = "# Fiber Plugin - produção: o que cada desenho tinha ao abrir (inicio) e a cada vez que foi salvo" +
                           Environment.NewLine + line;
                }
                System.IO.File.AppendAllText(path, line, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Anotações do mês de <paramref name="month"/>, na ordem em que foram feitas.</summary>
        public List<ProductionEntry> ReadMonth(DateTime month)
        {
            var entries = new List<ProductionEntry>();
            string path = FileFor(month);
            string[] lines;
            try
            {
                if (!System.IO.File.Exists(path)) return entries;
                lines = DataFiles.ReadAllLines(path);
            }
            catch (IOException) { return entries; }
            catch (UnauthorizedAccessException) { return entries; }

            foreach (string line in lines)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cols = line.Split('\t');
                if (cols.Length < 3 || !DateTime.TryParseExact(cols[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime when)) continue;
                entries.Add(new ProductionEntry { When = when, Event = cols[1], File = cols[2], Count = ProductionCount.FromFields(cols.Skip(3)) });
            }
            return entries.OrderBy(e => e.When).ToList();
        }

        public List<ProductionEntry> ReadDay(DateTime day) => ReadMonth(day).Where(e => e.When.Date == day.Date).ToList();

        /// <summary>Última anotação do desenho no dia (null se não tem).</summary>
        public ProductionEntry? Last(DateTime day, string file) =>
            ReadDay(day).LastOrDefault(e => string.Equals(e.File, file, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Desenho aberto agora, para a produção de hoje contar também o que ainda não foi salvo.</summary>
    public sealed class LiveDrawing
    {
        /// <summary>Arquivo do desenho; null se ainda não foi salvo nenhuma vez (Desenho1.dwg).</summary>
        public string? File { get; set; }
        public string Name { get; set; } = "";
        public ProductionCount Now { get; set; } = new ProductionCount();

        /// <summary>Começo do dia do desenho que ainda não foi salvo (o arquivo de produção só tem desenhos salvos).</summary>
        public ProductionCount? Start { get; set; }
    }

    /// <summary>Produção de um desenho no dia: o que ele tem no fim menos o que tinha no começo.</summary>
    public sealed class DrawingProduction
    {
        public string File { get; set; } = "";
        public string Name => Path.GetFileName(File);
        public ProductionCount Start { get; set; } = new ProductionCount();
        public ProductionCount End { get; set; } = new ProductionCount();
        public ProductionCount Done => End.Minus(Start);

        /// <summary>Aberto agora: o fim é o que ele tem agora, mesmo sem salvar.</summary>
        public bool Open { get; set; }

        /// <summary>Nunca foi salvo (Desenho1.dwg).</summary>
        public bool Unnamed { get; set; }
    }

    /// <summary>Produção de um dia, somando os desenhos em que se trabalhou.</summary>
    public sealed class DayProduction
    {
        public DateTime Day { get; private set; }

        /// <summary>Desenhos que mudaram no dia, do que mais produziu para o que menos.</summary>
        public List<DrawingProduction> Drawings { get; private set; } = new List<DrawingProduction>();

        /// <summary>Desenhos abertos no dia que não mudaram (só consulta).</summary>
        public int Unchanged { get; private set; }

        public ProductionCount Total { get; private set; } = new ProductionCount();

        /// <param name="entries">Anotações do arquivo de produção (as de outros dias são ignoradas).</param>
        /// <param name="open">Desenhos abertos agora; só valem para hoje (<paramref name="today"/>).</param>
        public static DayProduction For(DateTime day, IEnumerable<ProductionEntry> entries, IEnumerable<LiveDrawing> open, DateTime today)
        {
            var result = new DayProduction { Day = day.Date };
            var all = new List<DrawingProduction>();
            bool isToday = day.Date == today.Date;
            List<LiveDrawing> live = isToday ? open.ToList() : new List<LiveDrawing>();

            foreach (var group in entries.Where(e => e.When.Date == day.Date).GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase))
            {
                var drawing = new DrawingProduction { File = group.First().File, Start = group.First().Count, End = group.Last().Count };
                LiveDrawing? now = live.FirstOrDefault(l => string.Equals(l.File, drawing.File, StringComparison.OrdinalIgnoreCase));
                if (now != null)
                {
                    drawing.End = now.Now;
                    drawing.Open = true;
                }
                all.Add(drawing);
            }
            foreach (LiveDrawing unnamed in live.Where(l => l.File == null && l.Start != null))
            {
                all.Add(new DrawingProduction { File = unnamed.Name, Start = unnamed.Start!, End = unnamed.Now, Open = true, Unnamed = true });
            }

            result.Drawings = all.Where(d => !d.Done.IsZero).OrderByDescending(d => d.Done.CableTotal).ThenBy(d => d.Name).ToList();
            result.Unchanged = all.Count - result.Drawings.Count;
            result.Total = result.Drawings.Aggregate(new ProductionCount(), (sum, d) => sum.Plus(d.Done));
            return result;
        }

        /// <summary>Resumo em texto, para colar numa mensagem.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Produção de " + Day.ToString("dddd, dd/MM/yyyy", DataFiles.Br));
            AppendCount(sb, Total, "");
            if (Drawings.Count > 0)
            {
                sb.AppendLine("Desenhos:");
                foreach (DrawingProduction d in Drawings) sb.AppendLine($"  {d.Name}: {Describe(d.Done)}");
            }
            return sb.ToString();
        }

        private static void AppendCount(StringBuilder sb, ProductionCount c, string indent)
        {
            sb.AppendLine($"{indent}Cabo: {Meters(c.CableTotal)}");
            foreach (var cable in c.CableMeters.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"{indent}  {cable.Key}: {Meters(cable.Value)}");
            sb.AppendLine($"{indent}CTO: {c.Cto}");
            sb.AppendLine($"{indent}CEO: {c.Ceo}");
            sb.AppendLine($"{indent}Postes: {c.Poles}");
            if (c.Reserves != 0 || Math.Abs(c.ReserveMeters) >= 0.01)
                sb.AppendLine($"{indent}Reservas técnicas: {c.Reserves} ({Meters(c.ReserveMeters)})");
            if (c.Anchorings != 0) sb.AppendLine($"{indent}Amarrações: {c.Anchorings}");
            if (Math.Abs(c.StrandMeters) >= 0.01) sb.AppendLine($"{indent}Cordoalha: {Meters(c.StrandMeters)}");
        }

        /// <summary>"1.234,5 m de cabo · 3 CTO · 1 CEO · 12 postes" (só o que mudou).</summary>
        public static string Describe(ProductionCount c)
        {
            var parts = new List<string>();
            if (Math.Abs(c.CableTotal) >= 0.01) parts.Add($"{Meters(c.CableTotal)} de cabo");
            if (c.Cto != 0) parts.Add($"{c.Cto} CTO");
            if (c.Ceo != 0) parts.Add($"{c.Ceo} CEO");
            if (c.Poles != 0) parts.Add($"{c.Poles} poste{(Math.Abs(c.Poles) == 1 ? "" : "s")}");
            if (c.Anchorings != 0) parts.Add($"{c.Anchorings} amarraç{(Math.Abs(c.Anchorings) == 1 ? "ão" : "ões")}");
            if (c.Reserves != 0) parts.Add($"{c.Reserves} reserva{(Math.Abs(c.Reserves) == 1 ? "" : "s")}");
            if (Math.Abs(c.StrandMeters) >= 0.01) parts.Add($"{Meters(c.StrandMeters)} de cordoalha");
            return parts.Count > 0 ? string.Join(" · ", parts) : "sem mudança";
        }

        /// <summary>"1.234,5 m" (vírgula decimal).</summary>
        public static string Meters(double meters) => meters.ToString("#,##0.0", DataFiles.Br) + " m";
    }
}
