using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Forma do ícone de um ponto do KML (triângulo, círculo, quadrado...), para desenhar no AutoCAD o mesmo símbolo
    /// do Google Earth. Ordem: código do ícone do Google Earth web, nome do arquivo do ícone e, para ícones embutidos
    /// no KML/KMZ, a análise do contorno da imagem.
    /// </summary>
    public static class IconShapes
    {
        public const string Triangle = "TRIANGULO";
        public const string Circle = "CIRCULO";
        public const string Square = "QUADRADO";
        public const string Diamond = "LOSANGO";
        public const string Hexagon = "HEXAGONO";
        public const string Star = "ESTRELA";
        public const string Target = "ALVO";
        public const string Marker = "MARCADOR";
        public const string House = "CASA";
        public const string Point = "PONTO";

        /// <summary>Ícones do Google Earth web (earth.google.com/earth/document/icon?id=...).</summary>
        private static readonly Dictionary<int, string> EarthIds = new Dictionary<int, string>
        {
            [383] = Triangle, [251] = Circle, [304] = Target, [375] = Target, [308] = Square, [367] = Square,
            [317] = Hexagon, [168] = Marker, [2000] = Marker, [2176] = Marker, [69] = House
        };

        /// <summary>Palavras no nome do arquivo dos ícones clássicos (maps.google.com/mapfiles/kml/...), na ordem de teste.</summary>
        private static readonly (string Word, string Shape)[] Names =
        {
            ("paddle", Marker), ("pushpin", Marker), ("placemark_square", Square), ("placemark_circle", Circle),
            ("triangle", Triangle), ("donut", Circle), ("shaded_dot", Circle), ("target", Target), ("diamond", Diamond),
            ("star", Star), ("polygon", Hexagon), ("square", Square), ("home", House), ("house", House),
            ("circle", Circle), ("dot", Circle)
        };

        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        /// <summary>Forma do ícone <paramref name="href"/> de um arquivo KML/KMZ (PONTO se não der para saber).</summary>
        public static string Classify(string href, string kmlPath)
        {
            if (href.Length == 0) return Point;
            string key = kmlPath + "|" + href;
            if (Cache.TryGetValue(key, out string? cached)) return cached;

            string shape = FromEarthId(href) ?? FromName(href) ?? FromImage(ReadImage(href, kmlPath)) ?? Point;
            Cache[key] = shape;
            return shape;
        }

        private static string? FromEarthId(string href)
        {
            if (href.IndexOf("earth.google.com", StringComparison.OrdinalIgnoreCase) < 0) return null;
            Match m = Regex.Match(href, @"[?&]id=(\d+)");
            return m.Success && EarthIds.TryGetValue(int.Parse(m.Groups[1].Value), out string? shape) ? shape : null;
        }

        private static string? FromName(string href)
        {
            if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            string file = href.Split('?')[0].Replace('\\', '/');
            file = file.Substring(file.LastIndexOf('/') + 1).ToLowerInvariant();
            foreach (var (word, shape) in Names)
            {
                if (file.Contains(word)) return shape;
            }
            return null;
        }

        /// <summary>Bytes da imagem: embutida (data:), dentro do KMZ ou ao lado do KML. Null para endereços da internet.</summary>
        private static byte[]? ReadImage(string href, string kmlPath)
        {
            try
            {
                if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = href.IndexOf(',');
                    return comma > 0 && href.Substring(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                        ? Convert.FromBase64String(href.Substring(comma + 1))
                        : null;
                }
                if (Regex.IsMatch(href, "^[a-z]+://", RegexOptions.IgnoreCase)) return null;

                string relative = Uri.UnescapeDataString(href).Replace('\\', '/').TrimStart('/');
                if (Path.GetExtension(kmlPath).Equals(".kmz", StringComparison.OrdinalIgnoreCase))
                {
                    using (var file = new FileStream(kmlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                    {
                        ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(relative, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) return null;
                        using (Stream s = entry.Open())
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            return ms.ToArray();
                        }
                    }
                }
                string local = Path.Combine(Path.GetDirectoryName(kmlPath) ?? "", relative);
                return File.Exists(local) ? File.ReadAllBytes(local) : null;
            }
            catch (System.Exception ex) when (ex is IOException || ex is FormatException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Forma pelo contorno da imagem: largura de cada linha do desenho (pixels opacos e claros, sem a sombra)
        /// comparada em 5 alturas, área preenchida e proporção. Calibrado com os ícones do Google Earth.
        /// </summary>
        private static string? FromImage(byte[]? bytes)
        {
            if (bytes == null) return null;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var bitmap = new Bitmap(ms))
                {
                    int w0 = bitmap.Width, h0 = bitmap.Height;
                    var spans = new List<int>();
                    int left = int.MaxValue, right = -1, top = -1, bottom = -1;
                    var rowSpans = new (int L, int R)?[h0];
                    for (int y = 0; y < h0; y++)
                    {
                        int l = -1, r = -1;
                        for (int x = 0; x < w0; x++)
                        {
                            Color c = bitmap.GetPixel(x, y);
                            if (c.A <= 160 || Math.Max(c.R, Math.Max(c.G, c.B)) <= 60) continue;
                            if (l < 0) l = x;
                            r = x;
                        }
                        if (l < 0) continue;
                        rowSpans[y] = (l, r);
                        if (top < 0) top = y;
                        bottom = y;
                        left = Math.Min(left, l);
                        right = Math.Max(right, r);
                    }
                    if (top < 0) return null;

                    for (int y = top; y <= bottom; y++) spans.Add(rowSpans[y] is (int l, int r) ? r - l + 1 : 0);
                    double w = right - left + 1, h = bottom - top + 1;
                    double area = spans.Sum();
                    double P(double f) => spans[Math.Min(spans.Count - 1, (int)(f * (spans.Count - 1)))] / w;
                    double center = spans.Select((s, i) => (double)i * s).Sum() / area / Math.Max(1, h - 1);

                    double aspect = h / w, fill = area / (w * h);
                    double p5 = P(0.05), p25 = P(0.25), p50 = P(0.5), p75 = P(0.75), p95 = P(0.95);

                    if (aspect > 1.25 && p95 < 0.35 && center < 0.47) return Marker;          // gota: redonda em cima, ponta embaixo
                    if (fill > 0.88 && aspect > 0.8 && aspect < 1.25) return Square;
                    if (p5 < 0.35 && p95 > 0.75) return fill < 0.65 ? Triangle : House;       // ponta em cima, base larga
                    if (p5 < 0.35 && p50 > 0.85 && p95 < 0.35 && fill < 0.62) return Diamond;
                    if (p5 < 0.35 && p50 > 0.75 && p75 < 0.7 && p95 > 0.5) return Star;        // pontas no alto, dos lados e embaixo
                    if (aspect > 0.85 && aspect < 1.18 && fill >= 0.70 && fill <= 0.88 && Math.Abs(p25 - p75) < 0.12) return Circle;
                    return null;
                }
            }
            catch (ArgumentException)
            {
                return null; // Não é uma imagem que o Windows abra
            }
        }
    }
}
