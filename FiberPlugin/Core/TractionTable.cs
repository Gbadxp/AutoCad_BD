using System.Globalization;
using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>Linha da tabela de tração: faixa de fibras ("2-12", "96") e a tração (kgf) em cada vão da tabela.</summary>
    public sealed class TractionRow
    {
        public TractionRow(string label, double[] values)
        {
            Label = label;
            Values = values;
        }

        public string Label { get; }
        public double[] Values { get; }

        /// <summary>Limite superior da faixa ("2-12" → 12, "96" → 96). Null se o rótulo não terminar num número.</summary>
        public int? MaxFibers => int.TryParse(Label.Split('-', 'a', 'A').Last().Trim(), out int n) ? n : (int?)null;
    }

    /// <summary>
    /// Tabela 08 da NDU 009 (Energisa, versão 8.1): tração (kgf) de cabos de fibra óptica autossustentados, sem vento,
    /// com flecha de 1%, por faixa de número de fibras e vão. Vem no código; a janela Configurações (aba Tração) grava as
    /// alterações em Documentos\Fiber Plugin\tracao.txt. Sem esse arquivo, vale a planilha antiga Dados\tracao_ndu009.csv
    /// (até a 1.9.35) ou o padrão.
    /// </summary>
    public sealed class TractionTable
    {
        public const string FileName = "tracao.txt";
        public const string LegacyFileName = "tracao_ndu009.csv";

        public TractionTable(double[] spans, List<TractionRow> rows)
        {
            Spans = spans;
            Rows = rows;
        }

        /// <summary>Vãos das colunas, em metros (15 a 120 de 5 em 5 na norma).</summary>
        public double[] Spans { get; }

        public List<TractionRow> Rows { get; }

        public static TractionTable Default()
        {
            double[] spans = Enumerable.Range(0, 22).Select(i => 15.0 + 5 * i).ToArray();
            var rows = new List<TractionRow>
            {
                Row("2-12", 21, 28, 35, 42, 49, 56, 63, 70, 77, 84, 91, 98, 105, 112, 119, 126, 133, 140, 147, 154, 161, 168),
                Row("18-36", 21, 29, 36, 43, 50, 57, 64, 71, 78, 86, 93, 100, 107, 114, 121, 128, 135, 143, 150, 157, 164, 171),
                Row("48-72", 27, 36, 45, 54, 63, 73, 82, 91, 100, 109, 118, 127, 136, 145, 154, 163, 172, 181, 190, 199, 208, 218),
                Row("96", 35, 47, 59, 71, 82, 94, 106, 118, 129, 141, 153, 165, 176, 188, 200, 212, 223, 235, 247, 259, 270, 282),
                Row("120", 43, 57, 72, 86, 100, 115, 129, 143, 157, 172, 186, 200, 215, 229, 243, 258, 272, 286, 301, 315, 329, 344),
                Row("144", 53, 70, 88, 105, 123, 141, 158, 176, 193, 211, 228, 246, 263, 281, 299, 316, 334, 351, 369, 386, 404, 422)
            };
            return new TractionTable(spans, rows);
        }

        private static TractionRow Row(string label, params double[] values) => new TractionRow(label, values);

        /// <summary>
        /// Tabela em uso, sem depender do AutoCAD. Linhas com problema vão para <paramref name="warnings"/>; sem arquivo,
        /// ilegível ou sem nenhuma linha válida, vale o padrão (o motivo em <paramref name="error"/>).
        /// </summary>
        public static TractionTable Read(List<string> warnings, out string? error)
        {
            error = null;
            string? path = DataFiles.Source(FileName, LegacyFileName);
            if (path == null) return Default();

            double[] spans = new double[0];
            var rows = new List<TractionRow>();
            try
            {
                foreach (var (lineNumber, cols) in DataFiles.ReadRows(path))
                {
                    if (cols.Length < 3) continue;
                    if (spans.Length == 0)
                    {
                        // Cabeçalho: Fibras;15;20;...;120
                        spans = cols.Skip(1).Select(c => DataFiles.TryParseNumber(c, out double s) ? s : 0).ToArray();
                        continue;
                    }

                    var row = new TractionRow(cols[0], cols.Skip(1).Take(spans.Length)
                        .Select(c => DataFiles.TryParseNumber(c, out double t) ? t : 0).ToArray());
                    if (row.MaxFibers == null || row.Values.Length != spans.Length) warnings.Add($"linha {lineNumber} ignorada.");
                    else rows.Add(row);
                }
            }
            catch (IOException ex)
            {
                error = $"Não foi possível ler '{path}' ({ex.Message}); usando a Tabela 08 que vem com o plugin.";
                return Default();
            }

            if (rows.Count == 0 || spans.Length == 0 || spans.Any(s => s <= 0))
            {
                error = $"Tabela de tração inválida em '{path}'; usando a Tabela 08 que vem com o plugin.";
                return Default();
            }
            return new TractionTable(spans, rows);
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("# Tabela 08 da NDU 009: tração (kgf) de cabos de fibra óptica autossustentados, sem vento, flecha de 1%.\r\n");
            sb.Append("# Linhas: faixa de número de fibras; colunas: vão (m). Editada na janela Configurações do Fiber Plugin.\r\n");
            sb.Append("Fibras;").Append(string.Join(";", Spans.Select(s => s.ToString("0.###", DataFiles.Br)))).Append("\r\n");
            foreach (TractionRow row in Rows)
            {
                sb.Append(row.Label).Append(';').Append(string.Join(";", row.Values.Select(v => v.ToString("0.###", DataFiles.Br)))).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Grava a tabela. Retorna o erro (null se gravou).</summary>
        public string? Save() => DataFiles.Save(FileName, ToText(), LegacyFileName);

        /// <summary>Mesmos vãos, faixas e valores (para saber se a tabela mudou na janela).</summary>
        public bool SameAs(TractionTable other) => ToText() == other.ToText();

        /// <summary>Texto curto para mensagens: "6 faixas de fibras, vãos de 15 a 120 m".</summary>
        public string Summary =>
            $"{Rows.Count} faixa(s) de fibras, vãos de {Spans.Min().ToString("0.#", CultureInfo.CurrentCulture)} a {Spans.Max().ToString("0.#", CultureInfo.CurrentCulture)} m";
    }
}
