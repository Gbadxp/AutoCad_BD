using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Zona UTM do projeto (ex.: 20) e hemisfério.</summary>
    public sealed class UtmSettings
    {
        public int Zone { get; set; }
        public bool South { get; set; } = true;
    }

    /// <summary>
    /// Coordenadas UTM no formato do projeto:
    ///   20 L
    ///   405110.92 m E
    ///   9032585.41 m S
    /// A zona e o hemisfério ficam gravados no DWG; a letra da faixa de latitude é calculada por ponto.
    /// </summary>
    public static class UtmZone
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_UTM";
        private const string Bands = "CDEFGHJKLMNPQRSTUVWX"; // Faixas de 8° de latitude, de 80°S a 84°N

        public static UtmSettings? Get(Database db)
        {
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            if (v == null || v.Length < 2 || v[0].Value is not int zone || v[1].Value is not int south) return null;
            return zone >= 1 && zone <= 60 ? new UtmSettings { Zone = zone, South = south != 0 } : null;
        }

        public static void Set(Transaction tr, Database db, UtmSettings settings) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                new TypedValue((int)DxfCode.Int32, settings.Zone),
                new TypedValue((int)DxfCode.Int32, settings.South ? 1 : 0));

        /// <summary>Pergunta zona e hemisfério (sugerindo o atual, ou o da geolocalização do DWG) e grava.</summary>
        public static UtmSettings? Ask(Editor ed, Database db, UtmSettings? current)
        {
            UtmSettings? suggestion = current ?? FromGeoLocation(db);

            string hint = suggestion != null ? $" <{suggestion.Zone}>" : "";
            int? zone = CadHelpers.AskInt(ed, $"\nZona UTM (fuso) do projeto, ex.: 20, 22, 23{hint}: ", suggestion?.Zone, 1, 60);
            if (zone == null) return null;

            string hemisphere = suggestion?.South == false ? "Norte" : "Sul";
            string? answer = CadHelpers.AskKeyword(ed, $"\nHemisfério [Norte/Sul] <{hemisphere}>: ", "Norte Sul", hemisphere);
            if (answer == null) return null;

            var settings = new UtmSettings { Zone = zone.Value, South = answer == "Sul" };
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Set(tr, db, settings);
                tr.Commit();
            }
            return settings;
        }

        /// <summary>Tenta ler a zona da geolocalização do AutoCAD (ex.: sistema "UTM84-20S"). Null se não houver.</summary>
        public static UtmSettings? FromGeoLocation(Database db)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var geo = (GeoLocationData)tr.GetObject(db.GeoDataObject, OpenMode.ForRead);
                    Match m = Regex.Match(geo.CoordinateSystem ?? "", @"UTM\D{0,12}?(\d{1,2})\s*([NS])\b", RegexOptions.IgnoreCase);
                    if (!m.Success) return null;

                    int zone = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (zone < 1 || zone > 60) return null;
                    return new UtmSettings { Zone = zone, South = m.Groups[2].Value.Equals("S", StringComparison.OrdinalIgnoreCase) };
                }
            }
            catch
            {
                return null; // Desenho sem geolocalização
            }
        }

        /// <summary>"20 L": zona e letra da faixa de latitude do ponto.</summary>
        public static string ZoneText(Point3d point, UtmSettings utm)
        {
            double lat = Latitude(point.X, point.Y, utm.South);
            int index = (int)Math.Floor((lat + 80.0) / 8.0);
            return utm.Zone.ToString(CultureInfo.InvariantCulture) + " " + Bands[Math.Max(0, Math.Min(Bands.Length - 1, index))];
        }

        public static string EastingText(double x) => x.ToString("F2", CultureInfo.InvariantCulture) + " m E";

        public static string NorthingText(double y, bool south) =>
            y.ToString("F2", CultureInfo.InvariantCulture) + (south ? " m S" : " m N");

        /// <summary>Latitude em graus a partir de E/N UTM (WGS84/SIRGAS2000, fórmulas de Snyder).</summary>
        public static double Latitude(double easting, double northing, bool south)
        {
            const double k0 = 0.9996, a = 6378137.0, f = 1 / 298.257223563;
            double e2 = f * (2 - f), ep2 = e2 / (1 - e2);
            double x = easting - 500000.0;
            double y = northing - (south ? 10000000.0 : 0.0);

            double mu = y / k0 / (a * (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256));
            double e1 = (1 - Math.Sqrt(1 - e2)) / (1 + Math.Sqrt(1 - e2));
            double phi1 = mu
                + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
                + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
                + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu)
                + (1097 * Math.Pow(e1, 4) / 512) * Math.Sin(8 * mu);

            double sin = Math.Sin(phi1), cos = Math.Cos(phi1), tan = Math.Tan(phi1);
            double c1 = ep2 * cos * cos, t1 = tan * tan;
            double n1 = a / Math.Sqrt(1 - e2 * sin * sin);
            double r1 = a * (1 - e2) / Math.Pow(1 - e2 * sin * sin, 1.5);
            double d = x / (n1 * k0);

            double lat = phi1 - (n1 * tan / r1) * (d * d / 2
                - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * ep2) * Math.Pow(d, 4) / 24
                + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
            return lat * 180.0 / Math.PI;
        }
    }
}
