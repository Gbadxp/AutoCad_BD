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
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DictionaryKey)) return Default;

                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForRead);
                using (ResultBuffer? data = xrec.Data)
                {
                    TypedValue[]? values = data?.AsArray();
                    if (values != null && values.Length > 0 && values[0].Value is int scale && scale >= Min && scale <= Max)
                        return scale;
                }
                return Default;
            }
        }

        public static void Set(Transaction tr, Database db, int scale)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var data = new ResultBuffer(new TypedValue((int)DxfCode.Int32, scale));

            if (nod.Contains(DictionaryKey))
            {
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForWrite);
                xrec.Data = data;
            }
            else
            {
                nod.UpgradeOpen();
                var xrec = new Xrecord { Data = data };
                nod.SetAt(DictionaryKey, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
        }

        /// <summary>Multiplicador dos tamanhos de referência (1,0 em 1:1000).</summary>
        public static double Factor(Database db) => Get(db) / (double)Reference;

        // Tamanhos já ajustados para a escala do desenho
        public static double TextHeight(Database db) => FiberSettings.TextHeight * Factor(db);
        public static double LabelGap(Database db) => FiberSettings.LabelGap * Factor(db);
    }
}
