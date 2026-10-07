using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    /// <summary>O que pode ter escala própria (Configurações > Projeto > Por elemento).</summary>
    public enum ScaleItem
    {
        PoleIcon,   // Bloco do poste
        PoleText,   // Texto do poste (número, 11/300 e coordenadas)
        BoxIcon,    // Símbolo da CTO/CEO
        BoxText,    // Texto da CTO/CEO
        CableText,  // Nome do cabo e metragem nos vãos
        Effort      // Seta de esforço, com os textos dela
    }

    /// <summary>
    /// Escala de cada elemento (1:X), gravada no DWG. Zero = a escala do desenho. O bloco do poste é a exceção: sem escala
    /// própria ele entra no tamanho em que foi desenhado no BLOCOS.dwg (1:1000), como sempre foi.
    /// </summary>
    public sealed class ElementScales
    {
        private readonly Dictionary<ScaleItem, int> _values = new Dictionary<ScaleItem, int>
        {
            [ScaleItem.PoleIcon] = DrawingScale.Reference
        };

        /// <summary>Ordem em que os valores ficam gravados no DWG (não mudar: desenhos já salvos dependem dela).</summary>
        public static readonly ScaleItem[] Items =
            { ScaleItem.PoleIcon, ScaleItem.PoleText, ScaleItem.BoxIcon, ScaleItem.BoxText, ScaleItem.CableText, ScaleItem.Effort };

        /// <summary>Escala própria do elemento (0 = a do desenho).</summary>
        public int this[ScaleItem item]
        {
            get => _values.TryGetValue(item, out int v) ? v : 0;
            set => _values[item] = value;
        }

        /// <summary>Escala em uso: a própria ou, sem ela, a do desenho (<paramref name="drawing"/>).</summary>
        public int Effective(ScaleItem item, int drawing) => this[item] > 0 ? this[item] : drawing;

        public bool SameAs(ElementScales other) => Items.All(i => this[i] == other[i]);

        public ElementScales Clone()
        {
            var copy = new ElementScales();
            foreach (ScaleItem item in Items) copy[item] = this[item];
            return copy;
        }

        /// <summary>Os que têm escala própria, para o resumo: "postes 1:2000, cabos 1:500". Vazio se todos seguem o desenho.</summary>
        public string Summary(int drawing)
        {
            var parts = new List<string>();
            foreach (ScaleItem item in Items)
            {
                if (this[item] > 0 && this[item] != drawing) parts.Add($"{Label(item)} 1:{this[item]}");
            }
            return string.Join(", ", parts);
        }

        public static string Label(ScaleItem item) => item switch
        {
            ScaleItem.PoleIcon => "ícone dos postes",
            ScaleItem.PoleText => "texto dos postes",
            ScaleItem.BoxIcon => "ícone das CTO/CEO",
            ScaleItem.BoxText => "texto das CTO/CEO",
            ScaleItem.CableText => "nome dos cabos",
            _ => "setas de esforço"
        };
    }

    /// <summary>
    /// Escala do desenho (1:500, 1:1000, 1:2000...), gravada no próprio DWG para cada projeto guardar a sua.
    /// Os tamanhos de FiberSettings (texto, seta de esforço) valem para a escala de referência 1:1000;
    /// em outra escala eles são multiplicados por Factor (1:2000 → 2x, 1:500 → 0,5x). Cada elemento pode ter a sua
    /// escala (ElementScales): postes, CTO/CEO, nome dos cabos e setas, com ícone e texto separados.
    /// </summary>
    public static class DrawingScale
    {
        public const int Reference = 1000;
        public const int Default = 1000;
        public const int Min = 50;
        public const int Max = 100000;

        private const string DictionaryKey = "FIBRA_PLUGIN_ESCALA";
        private const string ElementsKey = "FIBRA_PLUGIN_ESCALAS";

        /// <summary>Denominador da escala (1000 para 1:1000). Padrão 1:1000 se o desenho ainda não tem escala definida.</summary>
        public static int Get(Database db)
        {
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, DictionaryKey);
            return v != null && v.Length > 0 && v[0].Value is int scale && scale >= Min && scale <= Max ? scale : Default;
        }

        public static void Set(Transaction tr, Database db, int scale) =>
            CadHelpers.WriteDrawingRecord(tr, db, DictionaryKey, new TypedValue((int)DxfCode.Int32, scale));

        /// <summary>Escalas por elemento do desenho (padrão: todas seguem a do desenho; o bloco do poste em 1:1000).</summary>
        public static ElementScales GetElements(Database db)
        {
            var scales = new ElementScales();
            TypedValue[]? v = CadHelpers.ReadDrawingRecord(db, ElementsKey);
            if (v == null) return scales;
            for (int i = 0; i < ElementScales.Items.Length && i < v.Length; i++)
            {
                if (v[i].Value is int s && (s == 0 || (s >= Min && s <= Max))) scales[ElementScales.Items[i]] = s;
            }
            return scales;
        }

        public static void SetElements(Transaction tr, Database db, ElementScales scales) =>
            CadHelpers.WriteDrawingRecord(tr, db, ElementsKey,
                ElementScales.Items.Select(i => new TypedValue((int)DxfCode.Int32, scales[i])).ToArray());

        /// <summary>Pergunta a escala 1:X (Enter = <paramref name="suggested"/>). Null se o usuário cancelar.</summary>
        public static int? Ask(Autodesk.AutoCAD.EditorInput.Editor ed, string message, int suggested) =>
            CadHelpers.AskInt(ed, $"{message} <{suggested}>: ", suggested, Min, Max);

        /// <summary>Multiplicador dos tamanhos de referência (1,0 em 1:1000).</summary>
        public static double Factor(Database db) => Get(db) / (double)Reference;

        /// <summary>Multiplicador do elemento: escala própria dele ou a do desenho.</summary>
        public static double Factor(Database db, ScaleItem item) => GetElements(db).Effective(item, Get(db)) / (double)Reference;

        // Tamanhos já ajustados para a escala do desenho
        public static double TextHeight(Database db) => FiberSettings.TextHeight * Factor(db);
        public static double LabelGap(Database db) => FiberSettings.LabelGap * Factor(db);

        // E para a escala do elemento
        public static double TextHeight(Database db, ScaleItem item) => FiberSettings.TextHeight * Factor(db, item);
        public static double LabelGap(Database db, ScaleItem item) => FiberSettings.LabelGap * Factor(db, item);
    }
}
