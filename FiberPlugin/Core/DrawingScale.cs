using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Escala do desenho (1:500, 1:1000, 1:2000...), gravada no próprio DWG para cada projeto guardar a sua.
    /// Os tamanhos de FiberSettings (texto, seta de esforço) valem para a escala de referência 1:1000;
    /// em outra escala eles são multiplicados por Factor (1:2000 → 2x, 1:500 → 0,5x).
    /// </summary>
    public static class DrawingScale
    {
        public const int Reference = 1000;
        public const int Default = 1000;
        public const int Min = 50;
        public const int Max = 100000;

        private const string DictionaryKey = "FIBRA_PLUGIN_ESCALA";

        /// <summary>Denominador da escala (1000 para 1:1000). Padrão 1:1000 se o desenho ainda não tem escala definida.</summary>
        public static int Get(Database db)
        {
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            return v != null && v.Length > 0 && v[0].Value is int scale && scale >= Min && scale <= Max ? scale : Default;
        }

        public static void Set(Transaction tr, Database db, int scale) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey, new TypedValue((int)DxfCode.Int32, scale));

        /// <summary>Pergunta a escala 1:X (Enter = <paramref name="suggested"/>). Null se o usuário cancelar.</summary>
        public static int? Ask(Autodesk.AutoCAD.EditorInput.Editor ed, string message, int suggested) =>
            CadHelpers.AskInt(ed, $"{message} <{suggested}>: ", suggested, Min, Max);

        /// <summary>Multiplicador dos tamanhos de referência (1,0 em 1:1000).</summary>
        public static double Factor(Database db) => Get(db) / (double)Reference;

        // Tamanhos já ajustados para a escala do desenho
        public static double TextHeight(Database db) => FiberSettings.TextHeight * Factor(db);
        public static double LabelGap(Database db) => FiberSettings.LabelGap * Factor(db);
    }
}
