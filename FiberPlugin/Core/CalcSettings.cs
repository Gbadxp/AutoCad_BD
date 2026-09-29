using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

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
            }
            return settings;
        }

        public void Save(Transaction tr, Database db) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey,
                new TypedValue((int)DxfCode.Real, AttachHeightM),
                new TypedValue((int)DxfCode.Int32, UseNormTable ? 1 : 0));

        /// <summary>Pergunta os parâmetros (sugerindo os atuais) e grava no DWG. Null se o usuário cancelar.</summary>
        public static CalcSettings? Ask(Editor ed, Database db)
        {
            CalcSettings current = Get(db);
            string suggested = current.AttachHeightM.ToString("0.00", CultureInfo.InvariantCulture);

            var pdo = new PromptDoubleOptions(
                $"\nAltura de fixação do cabo no poste, em m (NDU 009: {MinAttachHeight:0.00} a {MaxAttachHeight:0.00}) <{suggested}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult heightRes = ed.GetDouble(pdo);
            if (heightRes.Status == PromptStatus.Cancel) return null;
            double height = heightRes.Status == PromptStatus.OK ? heightRes.Value : current.AttachHeightM;
            if (height < MinAttachHeight || height > MaxAttachHeight)
            {
                ed.WriteMessage($"\n[AVISO]: {height:0.00} m está fora da faixa de ocupação da NDU 009 ({MinAttachHeight:0.00} a {MaxAttachHeight:0.00} m).");
            }

            string method = current.UseNormTable ? "Tabela" : "Peso";
            string? answer = CadHelpers.AskKeyword(ed, $"\nTração dos cabos [Tabela/Peso] <{method}>: ", "Tabela Peso", method);
            if (answer == null) return null;

            var settings = new CalcSettings { AttachHeightM = height, UseNormTable = answer == "Tabela" };
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                settings.Save(tr, db);
                tr.Commit();
            }
            return settings;
        }
    }
}
