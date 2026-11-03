using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// O que foi escolhido na janela do Gerar Folhas (fica gravado no DWG para a próxima vez): área do projeto, tamanho e
    /// orientação da folha, escala, sobreposição entre folhas vizinhas e prefixo dos nomes.
    /// </summary>
    public sealed class SheetSettings
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_FOLHAS";
        public const double MaxOverlapPercent = 50;

        // Orientação: a que der menos folhas, ou fixa
        public const int Automatic = 0, Landscape = 1, Portrait = 2;

        public string Format { get; set; } = "A2";
        public int Orientation { get; set; } = Automatic;

        /// <summary>Denominador da escala (1000 = 1:1000); 0 = a escala do desenho.</summary>
        public int Scale { get; set; }

        public double OverlapPercent { get; set; } = 5;
        public string Prefix { get; set; } = "FL";

        /// <summary>Área do projeto (metros, coordenadas do desenho); null se ainda não foi escolhida.</summary>
        public Rect? Area { get; set; }

        public SheetFormat SheetFormat => Core.SheetFormat.Find(Format) ?? Core.SheetFormat.All[2];

        /// <summary>Divide a área em folhas; null sem área ou sem escala definida.</summary>
        public SheetPlan? Plan(IReadOnlyList<Rect> content)
        {
            if (Area is not Rect area || Scale <= 0) return null;
            double overlap = OverlapPercent / 100.0;
            return Orientation == Automatic
                ? SheetPlanner.PlanBestOrientation(area, SheetFormat, Scale, overlap, content)
                : SheetPlanner.Plan(area, SheetFormat, Orientation == Landscape, Scale, overlap, content);
        }

        // ---------- Gravação no DWG ----------

        private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public string[] ToValues()
        {
            var values = new List<string>
            {
                Format, Orientation.ToString(CultureInfo.InvariantCulture), Scale.ToString(CultureInfo.InvariantCulture), N(OverlapPercent), Prefix
            };
            if (Area is Rect a) values.AddRange(new[] { N(a.MinX), N(a.MinY), N(a.MaxX), N(a.MaxY) });
            return values.ToArray();
        }

        public static SheetSettings FromValues(string[] v)
        {
            var s = new SheetSettings();
            if (v.Length < 5) return s;
            double D(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;

            if (Core.SheetFormat.Find(v[0]) != null) s.Format = v[0];
            if (int.TryParse(v[1], out int orientation) && orientation is >= Automatic and <= Portrait) s.Orientation = orientation;
            if (int.TryParse(v[2], out int scale) && scale >= DrawingScale.Min && scale <= DrawingScale.Max) s.Scale = scale;
            double overlap = D(v[3]);
            if (overlap >= 0 && overlap <= MaxOverlapPercent) s.OverlapPercent = overlap;
            if (v[4].Trim().Length > 0) s.Prefix = v[4].Trim();
            if (v.Length >= 9)
            {
                double minX = D(v[5]), minY = D(v[6]), maxX = D(v[7]), maxY = D(v[8]);
                if (maxX > minX && maxY > minY) s.Area = new Rect(minX, minY, maxX, maxY);
            }
            return s;
        }

        public static SheetSettings Load(Database db)
        {
            TypedValue[]? values = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            return FromValues(values?.Select(t => t.Value as string ?? "").ToArray() ?? Array.Empty<string>());
        }

        public void Save(Transaction tr, Database db) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey, ToValues().Select(v => new TypedValue((int)DxfCode.Text, v)).ToArray());
    }
}
