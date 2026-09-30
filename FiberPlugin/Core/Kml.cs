using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FiberPlugin.Core
{
    public enum KmlKind { Point, Line, Polygon }

    /// <summary>Um elemento lido do KML (um Placemark pode ter vários: MultiGeometry).</summary>
    public sealed class KmlFeature
    {
        public string Name { get; set; } = "";
        public string Folder { get; set; } = "";    // Pasta mais próxima no Google Earth ("" = raiz)
        public KmlKind Kind { get; set; }
        public List<(double Lon, double Lat)> Coords { get; } = new List<(double, double)>();

        // Estilo do Google Earth (vazio/null se não houver)
        public string IconHref { get; set; } = "";
        public string? IconColor { get; set; }      // aabbggrr
        public double IconScale { get; set; } = 1.0;
        public string? LineColor { get; set; }      // aabbggrr
    }

    /// <summary>
    /// Leitura e gravação de KML/KMZ (Google Earth). Lê pontos, linhas e polígonos de qualquer pasta,
    /// sem depender da versão do KML; grava pastas, estilos e descrições em HTML.
    /// </summary>
    public static class Kml
    {
        private static readonly XNamespace Ns = "http://www.opengis.net/kml/2.2";

        // ---------- Leitura ----------

        public static List<KmlFeature> Read(string path)
        {
            XDocument doc;
            if (Path.GetExtension(path).Equals(".kmz", StringComparison.OrdinalIgnoreCase))
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("doc.kml", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("O KMZ não tem nenhum arquivo .kml dentro.");
                    using (Stream stream = entry.Open()) doc = XDocument.Load(stream);
                }
            }
            else
            {
                doc = XDocument.Load(path);
            }

            // Estilos por id (Style, StyleMap e gx:CascadingStyle do Google Earth web, que usa kml:id)
            var styles = new Dictionary<string, XElement>();
            foreach (XElement e in doc.Descendants())
            {
                XAttribute? id = e.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");
                if (id != null && !styles.ContainsKey(id.Value)) styles[id.Value] = e;
            }

            var features = new List<KmlFeature>();
            foreach (XElement placemark in doc.Descendants().Where(e => e.Name.LocalName == "Placemark"))
            {
                string name = Child(placemark, "name")?.Value.Trim() ?? "";
                var style = new List<XElement>();
                if (Child(placemark, "Style") is XElement inline) style.Add(inline);
                if (ResolveStyle(styles, Child(placemark, "styleUrl")?.Value, 0) is XElement shared) style.Add(shared);
                string folder = placemark.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Folder") is XElement f
                    ? Child(f, "name")?.Value.Trim() ?? ""
                    : "";

                foreach (XElement geometry in placemark.Descendants())
                {
                    KmlKind? kind = geometry.Name.LocalName switch
                    {
                        "Point" => KmlKind.Point,
                        "LineString" => KmlKind.Line,
                        "Polygon" => KmlKind.Polygon,
                        _ => null
                    };
                    if (kind == null) continue;

                    // Polígono: só o contorno externo
                    XElement? coordinates = kind == KmlKind.Polygon
                        ? geometry.Descendants().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs")?
                              .Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates")
                        : Child(geometry, "coordinates");
                    if (coordinates == null) continue;

                    var feature = new KmlFeature
                    {
                        Name = name,
                        Folder = folder,
                        Kind = kind.Value,
                        IconHref = StyleValue(style, "IconStyle", "Icon", "href") ?? "",
                        IconColor = StyleValue(style, "IconStyle", "color"),
                        IconScale = double.TryParse(StyleValue(style, "IconStyle", "scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s > 0 ? s : 1.0,
                        LineColor = StyleValue(style, "LineStyle", "color")
                    };
                    feature.Coords.AddRange(ParseCoordinates(coordinates.Value));
                    int minimum = kind == KmlKind.Point ? 1 : 2;
                    if (feature.Coords.Count >= minimum) features.Add(feature);
                }
            }
            return features;
        }

        private static XElement? Child(XElement parent, string localName) =>
            parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

        /// <summary>Estilo "normal" de um styleUrl ("#id" ou "arquivo.kml#id"), passando por StyleMap e gx:CascadingStyle.</summary>
        private static XElement? ResolveStyle(Dictionary<string, XElement> styles, string? url, int depth)
        {
            if (string.IsNullOrWhiteSpace(url) || depth > 5) return null;
            string id = url!.Trim();
            int hash = id.LastIndexOf('#');
            if (hash >= 0) id = id.Substring(hash + 1);
            if (!styles.TryGetValue(id, out XElement? element)) return null;

            switch (element.Name.LocalName)
            {
                case "StyleMap":
                    XElement? pair = element.Elements().FirstOrDefault(p => p.Name.LocalName == "Pair" && Child(p, "key")?.Value.Trim() == "normal");
                    return pair == null ? null : Child(pair, "Style") ?? ResolveStyle(styles, Child(pair, "styleUrl")?.Value, depth + 1);
                case "CascadingStyle":
                    return Child(element, "Style");
                default:
                    return element;
            }
        }

        /// <summary>Primeiro valor do caminho (ex.: IconStyle/Icon/href) nos estilos, na ordem de prioridade.</summary>
        private static string? StyleValue(List<XElement> styles, params string[] path)
        {
            foreach (XElement style in styles)
            {
                XElement? e = style;
                foreach (string step in path)
                {
                    e = e == null ? null : Child(e, step);
                }
                if (e != null && e.Value.Trim().Length > 0) return e.Value.Trim();
            }
            return null;
        }

        /// <summary>"lon,lat[,alt] lon,lat[,alt] ..." → pares (lon, lat).</summary>
        private static IEnumerable<(double, double)> ParseCoordinates(string text)
        {
            foreach (string tuple in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = tuple.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    yield return (lon, lat);
                }
            }
        }

        // ---------- Gravação ----------

        /// <summary>Documento KML em montagem: estilos, pastas e marcadores.</summary>
        public sealed class Writer
        {
            private readonly XElement _document;

            public Writer(string name)
            {
                _document = new XElement(Ns + "Document", new XElement(Ns + "name", name));
            }

            /// <summary>Cor no formato do KML (aabbggrr).</summary>
            public static string Color(int r, int g, int b, int alpha = 255) => $"{alpha:x2}{b:x2}{g:x2}{r:x2}";

            public void IconStyle(string id, string iconHref, string color, double scale = 1.0, double labelScale = 0.8) =>
                _document.Add(new XElement(Ns + "Style", new XAttribute("id", id),
                    new XElement(Ns + "IconStyle",
                        new XElement(Ns + "color", color),
                        new XElement(Ns + "scale", scale.ToString(CultureInfo.InvariantCulture)),
                        new XElement(Ns + "Icon", new XElement(Ns + "href", iconHref))),
                    new XElement(Ns + "LabelStyle", new XElement(Ns + "scale", labelScale.ToString(CultureInfo.InvariantCulture)))));

            public void LineStyle(string id, string color, double width) =>
                _document.Add(new XElement(Ns + "Style", new XAttribute("id", id),
                    new XElement(Ns + "LineStyle",
                        new XElement(Ns + "color", color),
                        new XElement(Ns + "width", width.ToString(CultureInfo.InvariantCulture)))));

            public XElement Folder(string name)
            {
                var folder = new XElement(Ns + "Folder", new XElement(Ns + "name", name));
                _document.Add(folder);
                return folder;
            }

            public void Point(XElement folder, string name, string descriptionHtml, string styleId, double lon, double lat) =>
                folder.Add(Placemark(name, descriptionHtml, styleId,
                    new XElement(Ns + "Point", new XElement(Ns + "coordinates", Coordinate(lon, lat)))));

            public void Line(XElement folder, string name, string descriptionHtml, string styleId, IEnumerable<(double Lon, double Lat)> coords) =>
                folder.Add(Placemark(name, descriptionHtml, styleId,
                    new XElement(Ns + "LineString",
                        new XElement(Ns + "tessellate", 1),
                        new XElement(Ns + "coordinates", string.Join(" ", coords.Select(c => Coordinate(c.Lon, c.Lat)))))));

            /// <summary>Grava .kml ou, se a extensão for .kmz, o KML compactado (doc.kml).</summary>
            public void Save(string path)
            {
                var kml = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(Ns + "kml", _document));
                if (!Path.GetExtension(path).Equals(".kmz", StringComparison.OrdinalIgnoreCase))
                {
                    using (var writer = new StreamWriter(path, false, new UTF8Encoding(false))) kml.Save(writer);
                    return;
                }

                using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("doc.kml", CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                {
                    kml.Save(writer);
                }
            }

            private static XElement Placemark(string name, string descriptionHtml, string styleId, XElement geometry) =>
                new XElement(Ns + "Placemark",
                    new XElement(Ns + "name", name),
                    new XElement(Ns + "description", new XCData(descriptionHtml)),
                    new XElement(Ns + "styleUrl", "#" + styleId),
                    geometry);

            private static string Coordinate(double lon, double lat) =>
                lon.ToString("0.0000000", CultureInfo.InvariantCulture) + "," + lat.ToString("0.0000000", CultureInfo.InvariantCulture) + ",0";
        }

        /// <summary>Tabela HTML simples para a descrição dos marcadores (linhas com valor vazio ficam de fora).</summary>
        public static string Table(params (string Label, string Value)[] rows)
        {
            var html = new StringBuilder("<table style=\"font-family:Segoe UI,Arial;font-size:12px;border-collapse:collapse\">");
            foreach (var (label, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
            {
                html.Append("<tr><td style=\"color:#6B7785;padding:2px 10px 2px 0\">")
                    .Append(System.Net.WebUtility.HtmlEncode(label))
                    .Append("</td><td style=\"padding:2px 0\"><b>")
                    .Append(System.Net.WebUtility.HtmlEncode(value))
                    .Append("</b></td></tr>");
            }
            return html.Append("</table>").ToString();
        }
    }
}
