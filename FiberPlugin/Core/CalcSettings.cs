using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Parâmetros do cálculo de esforço, gravados no DWG (cada projeto guarda os seus):
    /// altura de fixação do cabo no poste e a origem da tração dos cabos.
    /// </summary>
    public sealed class CalcSettings
    {
        private const string DictionaryKey = "FIBRA_PLUGIN_CALCULO";

        /// <summary>Faixa de ocupação das redes de telecomunicações (NDU 009, item 8 a): 5,20 a 5,70 m do solo.</summary>
        public const double MinAttachHeight = 5.20;
        public const double MaxAttachHeight = 5.70;

        /// <summary>Altura do cabo no poste, em metros. Padrão 5,40 m: 3ª posição, a primeira para fibra óptica (Tabela 03).</summary>
        public double AttachHeightM { get; set; } = 5.40;

        /// <summary>True: tração pela Tabela 08 da NDU 009. False: pelo peso do cabo (flecha de 1%).</summary>
        public bool UseNormTable { get; set; } = true;

        /// <summary>Altura mínima do cabo ao solo no meio do vão, em m (Tabela 02 da NDU 009: ruas e avenidas 5,0 m).</summary>
        public double MinGroundClearanceM { get; set; } = 5.0;

        public string MethodText => UseNormTable
            ? "Tabela 08 da NDU 009 (cabo autossustentado, flecha de 1%)"
            : "Peso do cabo, flecha de 1% (T = p·L / 0,08)";

        public static CalcSettings Get(Database db)
        {
            var settings = new CalcSettings();
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            if (v != null && v.Length >= 2 && v[0].Value is double height && v[1].Value is int table)
            {
                if (height >= 1 && height <= 30) settings.AttachHeightM = height;
                settings.UseNormTable = table != 0;
                if (v.Length >= 3 && v[2].Value is double clearance && clearance > 0) settings.MinGroundClearanceM = clearance;
            }
            return settings;
        }

        public void Save(Transaction tr, Database db) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                new TypedValue((int)DxfCode.Real, AttachHeightM),
                new TypedValue((int)DxfCode.Int32, UseNormTable ? 1 : 0),
                new TypedValue((int)DxfCode.Real, MinGroundClearanceM));
    }
}
