using System.Globalization;
using System.Text.RegularExpressions;

namespace FiberPlugin.Core
{
    /// <summary>Retângulo do Importar Ruas já em UTM (metros), na zona em que as ruas serão desenhadas.</summary>
    public sealed class RoadArea
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public UtmSettings Utm { get; set; } = new UtmSettings();

        /// <summary>Zona calculada pelas coordenadas (o desenho não tinha e o campo ficou em branco).</summary>
        public bool ZoneFromCoordinates { get; set; }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    /// <summary>
    /// O que foi preenchido na janela do Importar Ruas, como texto (é o que fica gravado no DWG): área por
    /// retângulo (os quatro lados) ou centro e raio, em latitude/longitude (graus decimais) ou UTM (metros).
    /// </summary>
    public sealed class RoadImportSettings
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_RUAS";
        public const double MaxSideM = 50000;  // Acima disso o Overpass não responde a tempo
        public const double LargeSideM = 5000; // Acima disso o download pode demorar

        public bool Rectangle { get; set; } = true;
        public bool Geographic { get; set; } = true;  // false = UTM

        // Retângulo: latitude/longitude (graus) ou N/E (m)
        public string North { get; set; } = "";
        public string South { get; set; } = "";
        public string East { get; set; } = "";
        public string West { get; set; } = "";

        // Centro: latitude e longitude, ou E e N
        public string CenterA { get; set; } = "";
        public string CenterB { get; set; } = "";
        public string Radius { get; set; } = "1000";

        public string Zone { get; set; } = "";
        public bool SouthHemisphere { get; set; } = true;

        public string Style { get; set; } = "Contorno"; // Contorno, Eixo ou Ambos
        public bool IncludePaths { get; set; }
        public bool IncludeNames { get; set; }

        // ---------- Gravação no DWG ----------

        public string[] ToValues() => new[]
        {
            Rectangle ? "Retangulo" : "Centro", Geographic ? "LatLong" : "UTM", North, South, East, West,
            CenterA, CenterB, Radius, Zone, SouthHemisphere ? "S" : "N", Style, IncludePaths ? "1" : "0",
            IncludeNames ? "1" : "0"
        };

        public static RoadImportSettings FromValues(string[] v)
        {
            var s = new RoadImportSettings();
            if (v.Length < 13) return s;
            s.Rectangle = v[0] != "Centro";
            s.Geographic = v[1] != "UTM";
            s.North = v[2]; s.South = v[3]; s.East = v[4]; s.West = v[5];
            s.CenterA = v[6]; s.CenterB = v[7]; s.Radius = v[8];
            s.Zone = v[9];
            s.SouthHemisphere = v[10] != "N";
            s.Style = v[11] is "Eixo" or "Ambos" ? v[11] : "Contorno";
            s.IncludePaths = v[12] == "1";
            s.IncludeNames = v.Length > 13 && v[13] == "1"; // Gravado a partir da 1.9.21
            return s;
        }

        public static RoadImportSettings Load(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            var values = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            return FromValues(values?.Select(t => t.Value as string ?? "").ToArray() ?? Array.Empty<string>());
        }

        public void Save(Autodesk.AutoCAD.DatabaseServices.Transaction tr, Autodesk.AutoCAD.DatabaseServices.Database db) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                ToValues().Select(v => new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)Autodesk.AutoCAD.DatabaseServices.DxfCode.Text, v)).ToArray());

        // ---------- Leitura da área ----------

        /// <summary>
        /// Retângulo UTM pedido. Null se faltar algo (<paramref name="incomplete"/> = true, mensagem de orientação)
        /// ou se houver valor errado (mensagem de erro). Com área válida, a mensagem é um aviso ou vazia.
        /// </summary>
        public RoadArea? Resolve(out string message, out bool incomplete)
        {
            message = "";
            incomplete = false;

            // Zona: a digitada; em branco, só dá para calcular a partir de latitude/longitude
            UtmSettings? utm = null;
            if (Zone.Trim().Length > 0)
            {
                if (!int.TryParse(Zone.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone) || zone < 1 || zone > 60)
                {
                    message = "Zona UTM inválida: use um número de 1 a 60 (ex.: 20, 22, 23).";
                    return null;
                }
                utm = new UtmSettings { Zone = zone, South = SouthHemisphere };
            }
            else if (!Geographic)
            {
                message = "Informe a zona UTM das coordenadas (ex.: 20 Sul).";
                incomplete = true;
                return null;
            }

            RoadArea? area = Rectangle ? ResolveRectangle(ref utm, out message, out incomplete) : ResolveCenter(ref utm, out message, out incomplete);
            if (area == null) return null;

            // Longe do meridiano central: perto da borda da zona só avisa; além da zona vizinha é coordenada de outra região
            var (_, lon) = UtmZone.ToGeographic((area.MinX + area.MaxX) / 2, (area.MinY + area.MaxY) / 2, area.Utm.Zone, area.Utm.South);
            double offset = Math.Abs(lon - (area.Utm.Zone * 6 - 183));
            if (offset > 6)
            {
                message = $"A área fica fora da zona {area.Utm.Zone} (é da zona {UtmZone.ZoneFor(lon)}): confira a zona ou as coordenadas.";
                return null;
            }
            if (offset > 4) message = $"A área fica perto da borda da zona {area.Utm.Zone}: confira a zona UTM.";

            if (area.Width < 1 || area.Height < 1)
            {
                message = "Área pequena demais.";
                return null;
            }
            double side = Math.Max(area.Width, area.Height);
            if (side > MaxSideM)
            {
                message = $"Área grande demais ({side / 1000:N1} km de lado): o máximo é {MaxSideM / 1000:N0} km.";
                return null;
            }
            if (side > LargeSideM) message = (message.Length > 0 ? message + " " : "") + "Área grande: o download pode demorar.";
            return area;
        }

        private RoadArea? ResolveRectangle(ref UtmSettings? utm, out string message, out bool incomplete)
        {
            message = "";
            incomplete = false;
            if (new[] { North, South, East, West }.Any(t => t.Trim().Length == 0))
            {
                message = "Preencha os quatro lados: Norte, Sul, Leste e Oeste.";
                incomplete = true;
                return null;
            }

            if (Geographic)
            {
                double? n = Coordinates.ParseDegrees(North, latitude: true), s = Coordinates.ParseDegrees(South, latitude: true);
                double? e = Coordinates.ParseDegrees(East, latitude: false), w = Coordinates.ParseDegrees(West, latitude: false);
                if (WrongKind(out message, n, s, e, w)) return null;
                if (n == null || s == null || e == null || w == null) { message = "Use números em graus decimais, ex.: -3.1019."; return null; }
                if (!ValidLatitude(n.Value, out message) || !ValidLatitude(s.Value, out message) ||
                    !ValidLongitude(e.Value, out message) || !ValidLongitude(w.Value, out message)) return null;
                if (n.Value <= s.Value) { message = "O Norte (latitude) tem que ser maior que o Sul. No hemisfério sul, -3.09 fica ao norte de -3.11."; return null; }
                if (e.Value <= w.Value) { message = "O Leste (longitude) tem que ser maior que o Oeste. A oeste de Greenwich, -60.00 fica a leste de -60.03."; return null; }

                utm ??= AutoZone((n.Value + s.Value) / 2, (e.Value + w.Value) / 2);
                UtmSettings z = utm;
                // O retângulo UTM que cobre os quatro cantos (as linhas de lat/long não são paralelas à grade UTM)
                var corners = new[] { (n.Value, w.Value), (n.Value, e.Value), (s.Value, w.Value), (s.Value, e.Value) }
                    .Select(c => UtmZone.FromGeographic(c.Item1, c.Item2, z.Zone, z.South)).ToList();
                return Area(corners.Min(c => c.E), corners.Min(c => c.N), corners.Max(c => c.E), corners.Max(c => c.N), utm, Zone.Trim().Length == 0);
            }
            else
            {
                double? n = Coordinates.ParseMeters(North), s = Coordinates.ParseMeters(South);
                double? e = Coordinates.ParseMeters(East), w = Coordinates.ParseMeters(West);
                if (n == null || s == null || e == null || w == null) { message = "Use números em metros, ex.: 830710.50."; return null; }
                if (LooksGeographic(n, s, e, w)) { message = "Esses valores parecem latitude/longitude: escolha Lat/Long."; return null; }
                if (!ValidEasting(e.Value, out message) || !ValidEasting(w.Value, out message) ||
                    !ValidNorthing(n.Value, out message) || !ValidNorthing(s.Value, out message)) return null;
                if (n.Value <= s.Value) { message = "O Norte (N) tem que ser maior que o Sul."; return null; }
                if (e.Value <= w.Value) { message = "O Leste (E) tem que ser maior que o Oeste."; return null; }
                return Area(w.Value, s.Value, e.Value, n.Value, utm!, false);
            }
        }

        private RoadArea? ResolveCenter(ref UtmSettings? utm, out string message, out bool incomplete)
        {
            message = "";
            incomplete = false;
            if (CenterA.Trim().Length == 0 || CenterB.Trim().Length == 0 || Radius.Trim().Length == 0)
            {
                message = Geographic ? "Preencha latitude, longitude e raio." : "Preencha E, N e raio.";
                incomplete = true;
                return null;
            }
            double? radius = Coordinates.ParseMeters(Radius);
            if (radius == null || radius <= 0) { message = "Raio inválido: use metros, ex.: 1000."; return null; }

            double x, y;
            if (Geographic)
            {
                double? lat = Coordinates.ParseDegrees(CenterA, latitude: true), lon = Coordinates.ParseDegrees(CenterB, latitude: false);
                if (WrongKind(out message, lat, lon)) return null;
                if (lat == null || lon == null) { message = "Use números em graus decimais, ex.: -3.1019."; return null; }
                if (!ValidLatitude(lat.Value, out message) || !ValidLongitude(lon.Value, out message)) return null;
                utm ??= AutoZone(lat.Value, lon.Value);
                (x, y) = UtmZone.FromGeographic(lat.Value, lon.Value, utm.Zone, utm.South);
            }
            else
            {
                double? e = Coordinates.ParseMeters(CenterA), n = Coordinates.ParseMeters(CenterB);
                if (e == null || n == null) { message = "Use números em metros, ex.: 830710.50."; return null; }
                if (LooksGeographic(e, n)) { message = "Esses valores parecem latitude/longitude: escolha Lat/Long."; return null; }
                if (!ValidEasting(e.Value, out message) || !ValidNorthing(n.Value, out message)) return null;
                (x, y) = (e.Value, n.Value);
            }
            return Area(x - radius.Value, y - radius.Value, x + radius.Value, y + radius.Value, utm!, Geographic && Zone.Trim().Length == 0);
        }

        private static RoadArea Area(double minX, double minY, double maxX, double maxY, UtmSettings utm, bool auto) =>
            new RoadArea { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Utm = utm, ZoneFromCoordinates = auto };

        private static UtmSettings AutoZone(double lat, double lon) => new UtmSettings { Zone = UtmZone.ZoneFor(lon), South = lat < 0 };

        /// <summary>Lat/long com valor de UTM (centenas de milhares de metros).</summary>
        private static bool WrongKind(out string message, params double?[] values)
        {
            message = values.Any(v => v != null && Math.Abs(v.Value) > 180)
                ? "Esses valores parecem coordenadas UTM: escolha UTM (m)."
                : "";
            return message.Length > 0;
        }

        private static bool LooksGeographic(params double?[] values) => values.All(v => v != null && Math.Abs(v.Value) <= 180);

        private static bool ValidLatitude(double v, out string message)
        {
            message = v >= -80 && v <= 84 ? "" : "Latitude fora da faixa UTM (de -80 a 84).";
            return message.Length == 0;
        }

        private static bool ValidLongitude(double v, out string message)
        {
            message = v >= -180 && v <= 180 ? "" : "Longitude fora da faixa (de -180 a 180).";
            return message.Length == 0;
        }

        private static bool ValidEasting(double v, out string message)
        {
            message = v > 100000 && v < 900000 ? "" : "Coordenada E (leste) fora da faixa UTM: fica entre 100.000 e 900.000 m.";
            return message.Length == 0;
        }

        private static bool ValidNorthing(double v, out string message)
        {
            message = v >= 0 && v <= 10000000 ? "" : "Coordenada N (norte) fora da faixa UTM: fica entre 0 e 10.000.000 m.";
            return message.Length == 0;
        }
    }

    /// <summary>Leitura de coordenadas digitadas ou coladas, com ponto ou vírgula decimal.</summary>
    public static class Coordinates
    {
        /// <summary>
        /// Graus decimais: "-3.1019", "-3,1019", "3.1019 S", "60.025° W" / "O" (sul e oeste viram negativos).
        /// Null se não for um número.
        /// </summary>
        public static double? ParseDegrees(string text, bool latitude)
        {
            string t = text.Trim().ToUpperInvariant().Replace('−', '-');
            if (t.Length == 0) return null;
            bool negative = latitude ? Regex.IsMatch(t, @"\d.*\bS\b|\bS\s*\d|[\d°]\s*S\b|^S")
                                     : Regex.IsMatch(t, @"[\d°\s](W|O)\b|^(W|O)\b");
            double? value = Number(Regex.Replace(t, @"[^\d.,\-]", ""));
            if (value == null) return null;
            return negative ? -Math.Abs(value.Value) : value;
        }

        /// <summary>Metros: "830710.50", "830710,50", "9.656.678,58", "405110.92 m E", "9032585.41 m S".</summary>
        public static double? ParseMeters(string text)
        {
            string t = text.Trim().Replace('−', '-');
            return t.Length == 0 ? null : Number(Regex.Replace(t, @"[^\d.,\-]", ""));
        }

        /// <summary>Dois números colados juntos ("-3.1019, -60.0250", "830710 9656678"); null se não for um par.</summary>
        public static (string A, string B)? SplitPair(string text)
        {
            Match m = Regex.Match(text.Replace('−', '-'), @"^\s*(-?[\d.,]+?)\s*(?:;|,\s+|,(?=-)|\s+)\s*(-?[\d.,]+)\s*$");
            return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : ((string, string)?)null;
        }

        public static string Degrees(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
        public static string Meters(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>
        /// Número com ponto ou vírgula decimal. Com os dois, o último é o decimal ("9.656.678,58");
        /// vários pontos ou várias vírgulas são separadores de milhar.
        /// </summary>
        private static double? Number(string s)
        {
            if (s.Length == 0) return null;
            bool negative = s.StartsWith("-");
            s = s.Replace("-", "");
            int dot = s.LastIndexOf('.'), comma = s.LastIndexOf(',');
            if (dot >= 0 && comma >= 0)
            {
                char thousands = dot > comma ? ',' : '.';
                s = s.Replace(thousands.ToString(), "").Replace(',', '.');
            }
            else if (comma >= 0)
            {
                s = s.Count(c => c == ',') == 1 ? s.Replace(',', '.') : s.Replace(",", "");
            }
            else if (s.Count(c => c == '.') > 1)
            {
                s = s.Replace(".", "");
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
            return negative ? -v : v;
        }
    }
}
