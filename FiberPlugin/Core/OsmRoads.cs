using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Clipper2Lib;

namespace FiberPlugin.Core
{
    /// <summary>Uma via do OpenStreetMap (way com a tag highway), em latitude/longitude.</summary>
    public sealed class OsmRoad
    {
        public string Highway { get; set; } = "";
        public string Name { get; set; } = "";
        public string Ref { get; set; } = "";    // Sigla da rodovia (BR-174, AM-010...)
        public double WidthM { get; set; }

        /// <summary>O que escrever na rua: o nome, ou a sigla da rodovia se ela não tiver nome.</summary>
        public string Label => Name.Length > 0 ? Name : Ref;
        public List<(double Lat, double Lon)> Coords { get; } = new List<(double, double)>();
    }

    /// <summary>
    /// Ruas do OpenStreetMap pela API Overpass: download, largura aproximada de cada via, recorte na área
    /// pedida e leito das ruas unido (meio-fio sem sobreposição). As coordenadas de desenho (X, Y) são em metros.
    /// </summary>
    public static class OsmRoads
    {
        /// <summary>Servidor principal (com até 3 tentativas: ele recusa com 429/504 quando está cheio) e reservas.</summary>
        private static readonly (string Url, int Attempts)[] Servers =
        {
            ("https://overpass-api.de/api/interpreter", 3),
            ("https://overpass.kumi.systems/api/interpreter", 1),
            ("https://overpass.private.coffee/api/interpreter", 1)
        };

        /// <summary>Vias para veículos; as outras (calçadas, trilhas, ciclovias) só entram se pedidas.</summary>
        private static readonly string[] VehicleHighways =
        {
            "motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link",
            "tertiary", "tertiary_link", "unclassified", "residential", "living_street", "service"
        };

        /// <summary>Largura típica da via (m) quando o OSM não informa width nem lanes.</summary>
        private static readonly Dictionary<string, double> Widths = new Dictionary<string, double>
        {
            ["motorway"] = 18, ["motorway_link"] = 8, ["trunk"] = 16, ["trunk_link"] = 8, ["primary"] = 14, ["primary_link"] = 8,
            ["secondary"] = 12, ["secondary_link"] = 7, ["tertiary"] = 10, ["tertiary_link"] = 7, ["unclassified"] = 8,
            ["residential"] = 7, ["living_street"] = 6, ["service"] = 5, ["pedestrian"] = 4, ["track"] = 4,
            ["cycleway"] = 2.5, ["footway"] = 2, ["path"] = 2, ["steps"] = 2, ["bridleway"] = 2
        };

        public static bool IsVehicle(string highway) => VehicleHighways.Contains(highway);

        // ---------- Download ----------

        /// <summary>
        /// Vias dentro do retângulo geográfico (graus). Tenta os servidores Overpass um a um;
        /// IOException com o erro de cada servidor se nenhum responder. <paramref name="cancel"/> interrompe na hora,
        /// inclusive no meio de uma consulta (OperationCanceledException).
        /// </summary>
        public static List<OsmRoad> Download(double south, double west, double north, double east, bool onlyVehicles,
            CancellationToken cancel = default)
        {
            string filter = onlyVehicles ? "[\"highway\"~\"^(" + string.Join("|", VehicleHighways) + ")$\"]" : "[\"highway\"]";
            string query = "[out:xml][timeout:90];way" + filter +
                           FormattableString.Invariant($"({south},{west},{north},{east});") + "out geom;";
            byte[] body = Encoding.UTF8.GetBytes("data=" + Uri.EscapeDataString(query));

            var errors = new List<string>();
            foreach (var (server, attempts) in Servers)
            {
                string error = "";
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        XDocument doc = Post(server, body, cancel);
                        // O Overpass responde 200 com um <remark> quando estoura tempo ou memória
                        string? remark = doc.Root?.Element("remark")?.Value;
                        if (remark != null && remark.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                            throw new IOException(remark.Trim());
                        return Parse(doc).Where(r => !onlyVehicles || IsVehicle(r.Highway)).ToList();
                    }
                    catch (System.Exception ex) when (ex is WebException || ex is IOException || ex is XmlException)
                    {
                        cancel.ThrowIfCancellationRequested(); // Consulta abortada pelo cancelamento
                        error = ex.Message;
                        // Pausa antes de tentar de novo, interrompida se o usuário cancelar
                        if (attempt < attempts && cancel.WaitHandle.WaitOne(attempt * 3000)) cancel.ThrowIfCancellationRequested();
                    }
                }
                errors.Add($"{new Uri(server).Host}: {error}");
            }
            throw new IOException(string.Join("; ", errors));
        }

        private static XDocument Post(string url, byte[] body, CancellationToken cancel)
        {
#pragma warning disable SYSLIB0014 // WebRequest existe nas duas compilações (net48 não tem HttpClient sem referência extra)
            var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.UserAgent = $"FiberPlugin/{PluginInfo.Version} (AutoCAD)";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 100000;
            request.ReadWriteTimeout = 120000;

            // Cancelar aborta a conexão: a espera pela resposta termina na hora com WebException
            using (cancel.Register(request.Abort))
            {
                using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    return XDocument.Load(stream);
                }
            }
        }

        /// <summary>Lê a resposta "out geom": cada way já traz lat/lon em cada nd.</summary>
        private static List<OsmRoad> Parse(XDocument doc)
        {
            var roads = new List<OsmRoad>();
            foreach (XElement way in doc.Root?.Elements("way") ?? Enumerable.Empty<XElement>())
            {
                var tags = new Dictionary<string, string>();
                foreach (XElement tag in way.Elements("tag"))
                {
                    string? k = (string?)tag.Attribute("k");
                    if (k != null) tags[k] = (string?)tag.Attribute("v") ?? "";
                }
                if (!tags.TryGetValue("highway", out string? highway)) continue;

                var road = new OsmRoad
                {
                    Highway = highway,
                    Name = tags.TryGetValue("name", out string? name) ? name.Trim() : "",
                    Ref = tags.TryGetValue("ref", out string? reference) ? reference.Trim() : "",
                    WidthM = Width(tags, highway)
                };
                foreach (XElement nd in way.Elements("nd"))
                {
                    if (Number((string?)nd.Attribute("lat")) is double lat && Number((string?)nd.Attribute("lon")) is double lon)
                        road.Coords.Add((lat, lon));
                }
                if (road.Coords.Count >= 2) roads.Add(road);
            }
            return roads;
        }

        private static double? Number(string? text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;

        /// <summary>Largura da via: tag width ("7", "7 m", "23 ft"), senão lanes × 3,3 m, senão a típica do tipo.</summary>
        private static double Width(Dictionary<string, string> tags, string highway)
        {
            if (tags.TryGetValue("width", out string? width) && ParseWidth(width) is double w) return w;

            if (tags.TryGetValue("lanes", out string? lanes)
                && Number(lanes.Split(';')[0].Replace(',', '.').Trim()) is double count && count >= 1 && count <= 12)
                return Math.Max(4.5, count * 3.3);

            return Widths.TryGetValue(highway, out double typical) ? typical : 7.0;
        }

        private static double? ParseWidth(string value)
        {
            string text = value.ToLowerInvariant().Replace(',', '.').Trim();
            string digits = new string(text.SkipWhile(c => !char.IsDigit(c) && c != '.').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            if (Number(digits) is not double width) return null;
            if (text.Contains("ft") || text.Contains("feet") || text.Contains("'")) width *= 0.3048;
            return width >= 0.5 && width <= 80 ? width : (double?)null;
        }

        // ---------- Geometria (X, Y em metros) ----------

        /// <summary>
        /// Recorta a linha no retângulo: devolve os trechos que ficam dentro, cortados exatamente na borda
        /// (uma rua que sai e volta para a área vira dois trechos).
        /// </summary>
        public static List<List<(double X, double Y)>> Clip(IList<(double X, double Y)> line,
            double minX, double minY, double maxX, double maxY)
        {
            var runs = new List<List<(double X, double Y)>>();
            List<(double X, double Y)>? run = null;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                (double X, double Y) a = line[i], b = line[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y, t0 = 0, t1 = 1;

                // Liang-Barsky: limita t0..t1 a cada borda do retângulo
                bool Edge(double p, double q)
                {
                    if (p == 0) return q >= 0;
                    double r = q / p;
                    if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                    else { if (r < t0) return false; if (r < t1) t1 = r; }
                    return true;
                }

                if (!(Edge(-dx, a.X - minX) && Edge(dx, maxX - a.X) && Edge(-dy, a.Y - minY) && Edge(dy, maxY - a.Y)))
                {
                    run = null;
                    continue;
                }

                (double X, double Y) start = (a.X + t0 * dx, a.Y + t0 * dy), end = (a.X + t1 * dx, a.Y + t1 * dy);
                if (run == null || PlanarMath.Distance(run[run.Count - 1], start) > 1e-6)
                {
                    run = new List<(double X, double Y)> { start };
                    runs.Add(run);
                }
                run.Add(end);
                if (t1 < 1) run = null; // Saiu da área neste segmento
            }
            return runs.Where(r => r.Count >= 2).ToList();
        }

        /// <summary>
        /// Leito das ruas já "limpo": cada eixo vira uma faixa com a largura da via e as faixas são unidas, então
        /// os cruzamentos ficam abertos e sobra só o meio-fio (contorno externo e o de cada quadra). O resultado é
        /// cortado reto na borda do retângulo; os eixos podem passar da borda para a rua chegar inteira até ela.
        /// </summary>
        public static List<List<(double X, double Y)>> Pavement(IEnumerable<(IList<(double X, double Y)> Axis, double Width)> roads,
            double minX, double minY, double maxX, double maxY)
        {
            // Inteiros em milímetros a partir do canto da área (o Clipper trabalha com coordenadas inteiras)
            Point64 ToClipper((double X, double Y) p) => new Point64((long)Math.Round((p.X - minX) * Scale), (long)Math.Round((p.Y - minY) * Scale));

            var strips = new Paths64();
            foreach (var group in roads.GroupBy(r => Math.Round(Math.Max(r.Width, 1.0), 2)))
            {
                var offset = new ClipperOffset(arcTolerance: 0.05 * Scale); // Arcos das pontas e curvas com até 5 cm de erro
                offset.AddPaths(new Paths64(group.Select(r => new Path64(r.Axis.Select(ToClipper)))), JoinType.Round, EndType.Round);
                var strip = new Paths64();
                offset.Execute(group.Key / 2 * Scale, strip);
                strips.AddRange(strip);
            }

            Paths64 pavement = Clipper.Union(strips, FillRule.NonZero);

            // Fecha os vãos de menos de 60 cm (pistas paralelas quase encostadas deixariam lascas finas entre elas)
            pavement = Grow(pavement, CloseGap / 2 * Scale);
            pavement = Grow(pavement, -CloseGap / 2 * Scale);

            pavement = Clipper.RectClip(new Rect64(0, 0, (long)Math.Round((maxX - minX) * Scale), (long)Math.Round((maxY - minY) * Scale)), pavement);
            pavement = Clipper.SimplifyPaths(pavement, 0.01 * Scale); // Tira vértices alinhados (menos de 1 cm de desvio)

            return pavement.Where(p => p.Count >= 3 && Math.Abs(Clipper.Area(p)) >= MinArea * Scale * Scale)
                .Select(p => p.Select(q => (q.X / Scale + minX, q.Y / Scale + minY)).ToList())
                .ToList();
        }

        private const double Scale = 1000;   // Coordenadas do Clipper em mm
        private const double CloseGap = 0.6; // m
        private const double MinArea = 1.0;  // m²: sobras menores são descartadas

        /// <summary>Polígonos aumentados (delta positivo) ou diminuídos, com cantos redondos.</summary>
        private static Paths64 Grow(Paths64 polygons, double delta)
        {
            var offset = new ClipperOffset(arcTolerance: 0.05 * Scale);
            offset.AddPaths(polygons, JoinType.Round, EndType.Polygon);
            var result = new Paths64();
            offset.Execute(delta, result);
            return result;
        }
    }
}
