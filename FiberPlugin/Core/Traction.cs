using Autodesk.AutoCAD.EditorInput;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Tração de projeto de cada vão, em kgf. Pela Tabela 08 da NDU 009 (TractionTable, por número de fibras e vão)
    /// ou pelo peso do cabo com flecha de 1% (T = p·L² / 8f = p·L / 0,08).
    /// Cabos sem número de fibras conhecido usam o peso mesmo no modo tabela (com aviso).
    /// </summary>
    public sealed class Traction
    {
        private readonly List<(int MaxFibers, string Label, double[] Values)> _rows = new List<(int, string, double[])>();
        private double[] _spans = new double[0];

        public CalcSettings Settings { get; private set; } = new CalcSettings();

        /// <summary>True se a tração vem da tabela da norma (modo tabela e tabela carregada).</summary>
        public bool UsesTable => Settings.UseNormTable && _rows.Count > 0;

        /// <summary>Cabos calculados pelo peso no modo tabela, por não terem o número de fibras.</summary>
        public HashSet<string> WithoutFibers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Parâmetros do desenho e tabela de tração do plugin. Problemas com a tabela vão para o Editor.</summary>
        public static Traction Load(Autodesk.AutoCAD.DatabaseServices.Database db, Editor? ed = null)
        {
            var traction = new Traction { Settings = CalcSettings.Get(db) };
            if (!traction.Settings.UseNormTable) return traction;

            var warnings = new List<string>();
            TractionTable table = TractionTable.Read(warnings, out string? error);
            if (error != null) ed?.WriteMessage($"\n[AVISO]: {error}");
            foreach (string warning in warnings) ed?.WriteMessage($"\n[AVISO] Tabela de tração: {warning}");

            // "2-12", "18-36", "96"...: vale o limite superior da faixa
            traction._spans = table.Spans;
            foreach (TractionRow row in table.Rows)
            {
                if (row.MaxFibers is int maxFibers) traction._rows.Add((maxFibers, row.Label, row.Values));
            }
            traction._rows.Sort((a, b) => a.MaxFibers.CompareTo(b.MaxFibers));
            return traction;
        }

        /// <summary>Resumo do método (início dos comandos) e aviso dos cabos sem número de fibras (fim).</summary>
        public void WriteMethod(Editor ed) =>
            ed.WriteMessage($"\n[INFO]: Tração:{(UsesTable ? Settings.MethodText : new CalcSettings { UseNormTable = false }.MethodText)}; " +
                            $"cabo a {Settings.AttachHeightM:0.00} m, esforço transferido a 20 cm do topo (NDU 009).");

        public void WriteWarnings(Editor ed)
        {
            if (WithoutFibers.Count == 0) return;
            ed.WriteMessage($"\n[AVISO]: Sem o número de fibras, calculados pelo peso: {string.Join(", ", WithoutFibers)}. " +
                            "Preencha a coluna Fibras em Configurações > Cabos.");
        }

        /// <summary>Tração do vão de <paramref name="span"/> metros para o cabo, em kgf.</summary>
        public double Tension(CableModel cable, double span)
        {
            if (Settings.UseNormTable && _rows.Count > 0)
            {
                if (cable.Fibers is int fibers) return FromTable(fibers, span);
                WithoutFibers.Add(cable.ShortName);
            }
            return cable.WeightKgKm / 1000.0 * span / (8.0 * FiberSettings.SagRatio);
        }

        /// <summary>Como a tração deste cabo é obtida (para o memorial): "Tabela 08 (2-12 fibras)" ou "peso 31 kg/km".</summary>
        public string Describe(CableModel cable)
        {
            if (UsesTable && cable.Fibers is int fibers) return $"Tabela 08 da NDU 009 ({Row(fibers).Label} fibras)";
            return $"Peso de {cable.WeightKgKm:0.#} kg/km, flecha de 1%";
        }

        /// <summary>Faixa da tabela: a primeira cujo limite superior cobre o número de fibras (a maior, acima de todas).</summary>
        private (int MaxFibers, string Label, double[] Values) Row(int fibers)
        {
            foreach (var row in _rows)
            {
                if (row.MaxFibers >= fibers) return row;
            }
            return _rows[_rows.Count - 1];
        }

        /// <summary>Interpolação linear entre os vãos da tabela; fora dela, proporcional ao vão (a tabela é linear).</summary>
        private double FromTable(int fibers, double span)
        {
            double[] t = Row(fibers).Values;
            int last = _spans.Length - 1;
            if (span <= _spans[0]) return t[0] * span / _spans[0];
            if (span >= _spans[last]) return t[last] * span / _spans[last];

            int i = 1;
            while (_spans[i] < span) i++;
            double k = (span - _spans[i - 1]) / (_spans[i] - _spans[i - 1]);
            return t[i - 1] + k * (t[i] - t[i - 1]);
        }
    }
}
