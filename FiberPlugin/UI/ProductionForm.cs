using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Produção do dia: metros de cabo (no total e por tipo), CTO, CEO, postes, reservas técnicas e cordoalha feitos no dia, somando
    /// os desenhos em que se trabalhou. Navega pelos dias, copia o resumo em texto e gera o Excel do mês.
    /// </summary>
    internal class ProductionForm : Form
    {
        private readonly ProductionLog _log;
        private readonly List<LiveDrawing> _open;
        private DateTime _day = DateTime.Today;
        private DayProduction _production = null!;

        private readonly HeaderPanel _header;
        private readonly Label _dayLabel;
        private readonly ThemedButton _next;
        private readonly StatTile _cable, _cto, _ceo, _poles, _reserves, _strand;
        private readonly ThemedListBox _cables, _drawings;
        private readonly SectionLabel _drawingsTitle;
        private readonly FooterPanel _footer;

        /// <param name="open">Desenhos abertos agora, com o que têm (a produção de hoje conta o que ainda não foi salvo).</param>
        public ProductionForm(ProductionLog log, List<LiveDrawing> open)
        {
            _log = log;
            _open = open;

            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Produção do Dia";
            ClientSize = new Size(700, 560);

            _header = new HeaderPanel { IconCommand = "FIBRA_PRODUCAO", Title = "Produção do Dia" };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(18, 10, 18, 8),
                BackColor = Theme.Background
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Dia: anterior, o dia, próximo e hoje
            var previous = new ThemedButton("◀", false) { Size = new Size(36, 28), Anchor = AnchorStyles.Left };
            _next = new ThemedButton("▶", false) { Size = new Size(36, 28), Anchor = AnchorStyles.Right };
            var today = new ThemedButton("Hoje", false) { Anchor = AnchorStyles.Right };
            _dayLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.BodyBold,
                ForeColor = Theme.TextStrong,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true
            };
            var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            nav.Controls.Add(previous, 0, 0);
            nav.Controls.Add(_dayLabel, 1, 0);
            nav.Controls.Add(_next, 2, 0);
            nav.Controls.Add(today, 3, 0);
            previous.Click += (s, e) => ShowDay(_day.AddDays(-1));
            _next.Click += (s, e) => ShowDay(_day.AddDays(1));
            today.Click += (s, e) => ShowDay(DateTime.Today);

            // Números do dia
            _cable = new StatTile("Cabo");
            _cto = new StatTile("CTO");
            _ceo = new StatTile("CEO");
            _poles = new StatTile("Postes");
            _reserves = new StatTile("Reservas");
            _strand = new StatTile("Cordoalha");
            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Theme.Background, Margin = new Padding(0, 4, 0, 4) };
            tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            StatTile[] all = { _cable, _cto, _ceo, _poles, _reserves, _strand };
            for (int i = 0; i < all.Length; i++)
            {
                tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i == 0 || i == all.Length - 1 ? 21 : 14.5f));
                all[i].Dock = DockStyle.Fill;
                all[i].Margin = new Padding(i == 0 ? 0 : 4, 0, i == all.Length - 1 ? 0 : 4, 0);
                tiles.Controls.Add(all[i], i, 0);
            }

            // Cabos por tipo e desenhos do dia
            _cables = new ThemedListBox
            {
                Dock = DockStyle.Fill,
                LogicalItemHeight = 30,
                Margin = new Padding(0, 0, 6, 0),
                Describe = item => item is (string name, double meters) ? (name, null, DayProduction.Meters(meters)) : (item.ToString() ?? "", null, null)
            };
            _drawings = new ThemedListBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0),
                Describe = item => item is DrawingProduction d
                    ? (d.Name, DayProduction.Describe(d.Done), d.Unnamed ? "não salvo" : d.Open ? "aberto" : null)
                    : (item.ToString() ?? "", null, null)
            };
            var cablesTitle = new SectionLabel("Cabo lançado por tipo") { Dock = DockStyle.Fill };
            _drawingsTitle = new SectionLabel("Desenhos") { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };

            body.Controls.Add(nav, 0, 0);
            body.SetColumnSpan(nav, 2);
            body.Controls.Add(tiles, 0, 1);
            body.SetColumnSpan(tiles, 2);
            body.Controls.Add(cablesTitle, 0, 2);
            body.Controls.Add(_drawingsTitle, 1, 2);
            body.Controls.Add(_cables, 0, 3);
            body.Controls.Add(_drawings, 1, 3);

            _footer = new FooterPanel { Hint = "O que cada desenho ganhou no dia, desde que foi aberto" };
            var close = new ThemedButton("Fechar", false) { DialogResult = DialogResult.Cancel };
            var excel = new ThemedButton("Excel do Mês", false);
            var copy = new ThemedButton("Copiar Resumo", true);
            _footer.AddButton(close);
            _footer.AddButton(excel);
            _footer.AddButton(copy);
            copy.Click += (s, e) => CopySummary();
            excel.Click += (s, e) => SaveMonth();

            Controls.Add(body);
            Controls.Add(_header);
            Controls.Add(_footer);
            CancelButton = close;
            ShowDay(DateTime.Today);
        }

        private void ShowDay(DateTime day)
        {
            _day = day.Date > DateTime.Today ? DateTime.Today : day.Date;
            _production = DayProduction.For(_day, _log.ReadDay(_day), _open, DateTime.Today);
            ProductionCount total = _production.Total;

            string name = _day.ToString("dddd, dd 'de' MMMM 'de' yyyy", DataFiles.Br);
            _dayLabel.Text = char.ToUpper(name[0], DataFiles.Br) + name.Substring(1);
            _next.Enabled = _day < DateTime.Today;
            _header.Subtitle = _production.Drawings.Count == 0
                ? (_day == DateTime.Today ? "Nada feito hoje ainda" : "Nada anotado neste dia")
                : DayProduction.Describe(total);
            _header.Invalidate();

            _cable.Set(total.CableTotal.ToString("#,##0.0", DataFiles.Br), "metros", total.CableTotal < -0.005);
            _cto.Set(total.Cto.ToString(CultureInfo.InvariantCulture), "", total.Cto < 0);
            _ceo.Set(total.Ceo.ToString(CultureInfo.InvariantCulture), "", total.Ceo < 0);
            _poles.Set(total.Poles.ToString(CultureInfo.InvariantCulture), "", total.Poles < 0);
            _reserves.Set(total.Reserves.ToString(CultureInfo.InvariantCulture),
                Math.Abs(total.ReserveMeters) >= 0.01 ? DayProduction.Meters(total.ReserveMeters) : "", total.Reserves < 0);
            _strand.Set(total.StrandMeters.ToString("#,##0.0", DataFiles.Br), "metros", total.StrandMeters < -0.005);

            _cables.Items.Clear();
            foreach (var cable in total.CableMeters.OrderByDescending(c => c.Value))
                _cables.Items.Add((cable.Key, cable.Value));
            if (_cables.Items.Count == 0) _cables.Items.Add("Nenhum cabo lançado");

            _drawings.Items.Clear();
            foreach (DrawingProduction d in _production.Drawings) _drawings.Items.Add(d);
            if (_drawings.Items.Count == 0) _drawings.Items.Add("Nenhum desenho mudou");
            _drawingsTitle.Text = ("Desenhos" + (_production.Unchanged > 0 ? $" · {_production.Unchanged} aberto(s) sem mudança" : "")).ToUpperInvariant();
        }

        private void CopySummary()
        {
            try
            {
                Clipboard.SetText(_production.ToText());
                _footer.Hint = "Resumo copiado: é só colar na mensagem";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                _footer.Hint = "Não deu para copiar agora: tente de novo";
            }
        }

        /// <summary>Excel do mês do dia mostrado: produção de cada dia, de cada desenho e de cada tipo de cabo.</summary>
        private void SaveMonth()
        {
            DateTime month = new DateTime(_day.Year, _day.Month, 1);
            string? path = CadHelpers.AskSavePath("Salvar Produção do Mês", $"Producao_{month:yyyy-MM}.xlsx", "Planilha do Excel (*.xlsx)|*.xlsx");
            if (path == null) return;

            List<ProductionEntry> entries = _log.ReadMonth(month);
            var days = entries.Select(e => e.When.Date).Distinct().ToList();
            if (month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month && !days.Contains(DateTime.Today)) days.Add(DateTime.Today);
            List<DayProduction> production = days.OrderBy(d => d)
                .Select(d => DayProduction.For(d, entries, _open, DateTime.Today))
                .Where(p => p.Drawings.Count > 0)
                .ToList();

            string monthName = month.ToString("MMMM 'de' yyyy", DataFiles.Br);
            double R(double v) => Math.Round(v, 1);
            var workbook = new XlsxWriter();

            XlsxWriter.Sheet byDay = workbook.AddSheet("Por dia").ColumnWidths(14, 16, 14, 8, 8, 9, 10, 14, 15, 10);
            byDay.Title("Produção de " + monthName).Blank()
                 .Header("Data", "Dia", "Cabo (m)", "CTO", "CEO", "Postes", "Reservas", "Reservas (m)", "Cordoalha (m)", "Desenhos");
            foreach (DayProduction p in production)
            {
                ProductionCount t = p.Total;
                byDay.Row(p.Day.ToString("dd/MM/yyyy", DataFiles.Br), p.Day.ToString("dddd", DataFiles.Br), R(t.CableTotal),
                          t.Cto, t.Ceo, t.Poles, t.Reserves, R(t.ReserveMeters), R(t.StrandMeters), p.Drawings.Count);
            }
            ProductionCount monthTotal = production.Aggregate(new ProductionCount(), (sum, p) => sum.Plus(p.Total));
            byDay.Row("Total do mês", $"{production.Count} dia(s)", R(monthTotal.CableTotal), monthTotal.Cto, monthTotal.Ceo, monthTotal.Poles,
                      monthTotal.Reserves, R(monthTotal.ReserveMeters), R(monthTotal.StrandMeters), "");

            XlsxWriter.Sheet byDrawing = workbook.AddSheet("Por desenho").ColumnWidths(14, 36, 14, 8, 8, 9, 10, 15, 60);
            byDrawing.Title("Produção de " + monthName + " por desenho").Blank()
                     .Header("Data", "Desenho", "Cabo (m)", "CTO", "CEO", "Postes", "Reservas", "Cordoalha (m)", "Pasta");
            foreach (DayProduction p in production)
            {
                foreach (DrawingProduction d in p.Drawings)
                {
                    ProductionCount c = d.Done;
                    byDrawing.Row(p.Day.ToString("dd/MM/yyyy", DataFiles.Br), d.Name, R(c.CableTotal), c.Cto, c.Ceo, c.Poles, c.Reserves,
                                  R(c.StrandMeters), d.Unnamed ? "(não salvo)" : Path.GetDirectoryName(d.File) ?? "");
                }
            }

            XlsxWriter.Sheet byCable = workbook.AddSheet("Cabos").ColumnWidths(14, 30, 14);
            byCable.Title("Cabo lançado em " + monthName).Blank().Header("Data", "Cabo", "Metros");
            foreach (DayProduction p in production)
            {
                foreach (var cable in p.Total.CableMeters.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                    byCable.Row(p.Day.ToString("dd/MM/yyyy", DataFiles.Br), cable.Key, R(cable.Value));
            }
            byCable.Blank().Header("Total do mês", "Cabo", "Metros");
            foreach (var cable in monthTotal.CableMeters.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                byCable.Row("", cable.Key, R(cable.Value));

            try
            {
                workbook.Save(path);
            }
            catch (IOException ex)
            {
                _footer.Hint = $"Não deu para salvar ({ex.Message}): se estiver aberta no Excel, feche e tente de novo";
                return;
            }
            _footer.Hint = "Excel salvo em " + path;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.Exception)
            {
                // Sem Excel associado: o arquivo continua salvo
            }
        }

        /// <summary>Quadro de um número do dia: título em cima, número grande e detalhe embaixo.</summary>
        private sealed class StatTile : Control
        {
            private static readonly Font ValueFont = new Font("Segoe UI Semibold", 17f);
            private readonly string _caption;
            private string _value = "0";
            private string _detail = "";
            private bool _negative;

            public StatTile(string caption)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                _caption = caption.ToUpperInvariant();
                BackColor = Theme.Background;
            }

            /// <param name="negative">Saldo negativo (mais apagado que feito): o número fica em vermelho.</param>
            public void Set(string value, string detail, bool negative)
            {
                _value = value;
                _detail = detail;
                _negative = negative;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);
                int S(int v) => Theme.Scale(this, v);
                var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                Theme.FillRounded(g, Theme.Surface, r, S(Theme.Radius));
                Theme.DrawRounded(g, Theme.Border, r, S(Theme.Radius));

                const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int left = S(10), width = Width - S(20);
                TextRenderer.DrawText(g, _caption, Theme.Section, new Rectangle(left, S(8), width, Theme.Section.Height), Theme.Muted, flags);
                TextRenderer.DrawText(g, _value, ValueFont, new Rectangle(left, S(24), width, ValueFont.Height + S(2)),
                    _negative ? Theme.Error : Theme.TextStrong, flags);
                if (_detail.Length > 0)
                {
                    TextRenderer.DrawText(g, _detail, Theme.Small, new Rectangle(left, Height - S(8) - Theme.Small.Height, width, Theme.Small.Height),
                        Theme.Muted, flags);
                }
            }
        }
    }
}
