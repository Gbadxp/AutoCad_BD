using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Parâmetros do cálculo de esforço (NDU 009), gravados no desenho: altura de fixação do cabo no poste, origem da
    /// tração (Tabela 08 da norma ou peso do cabo) e altura mínima do cabo ao solo, com atalhos da Tabela 02.
    /// Confere os valores enquanto o usuário digita.
    /// </summary>
    internal class ParametersForm : Form
    {
        private static readonly double[] Clearances = { 5.0, 4.5, 3.0 }; // Tabela 02: ruas, rural com veículos, pedestres

        private readonly LabeledInput _height;
        private readonly ChoiceBar _method;
        private readonly Label _methodHint;
        private readonly LabeledInput _clearance;
        private readonly ChoiceBar _presets;
        private readonly Label _status;
        private readonly ThemedButton _ok;
        private bool _syncing;

        /// <summary>Parâmetros escolhidos (null enquanto algum valor estiver errado).</summary>
        public CalcSettings? Result { get; private set; }

        public ParametersForm(CalcSettings current)
        {
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Parâmetros de Cálculo";
            ClientSize = new Size(600, 556);

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_PARAMETROS",
                Title = "Parâmetros de Cálculo",
                Subtitle = "Esforço nos postes pela NDU 009 · gravados neste desenho"
            };

            _height = new LabeledInput("Altura de fixação (m)", "ex.: 5.40") { Anchor = AnchorStyles.Left, Width = 300 };
            _method = new ChoiceBar("Tabela 08 da NDU 009", "Peso do cabo");
            _methodHint = Hint("");
            _clearance = new LabeledInput("Altura mínima ao solo (m)", "ex.: 5.0") { Anchor = AnchorStyles.Left, Width = 300 };
            _presets = new ChoiceBar("Ruas e avenidas · 5,0 m", "Rural com veículos · 4,5 m", "Só pedestres · 3,0 m");
            _status = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted };

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 4, 18, 6), BackColor = Theme.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (control, rowHeight) in new (Control, float)[]
            {
                (Section("Fixação no poste"), 26), (_height, 56),
                (Hint($"NDU 009, item 8 a: de {Format(CalcSettings.MinAttachHeight, "0.00")} a {Format(CalcSettings.MaxAttachHeight, "0.00")} m do solo. " +
                      "Padrão 5,40 m: 3ª posição, a primeira para fibra óptica (Tabela 03)."), 40),
                (Section("Tração dos cabos"), 30), (_method, 38), (_methodHint, 40),
                (Section("Altura do cabo ao solo"), 30), (_clearance, 56), (_presets, 38),
                (Hint("Tabela 02 da NDU 009. O Verificar Projeto avisa os vãos em que, com flecha de 1%, o cabo fica abaixo disso."), 40),
                (_status, 46)
            })
            {
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                if (control.Anchor == AnchorStyles.Left) control.Margin = new Padding(0, 2, 0, 0);
                else control.Dock = DockStyle.Fill;
                body.Controls.Add(control, 0, body.RowCount++);
            }
            // Sobra de altura numa linha vazia no fim (senão a última linha estica)
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowCount++;

            var footer = new FooterPanel { Hint = "Recalcule o Esforço no Percurso depois" };
            _ok = new ThemedButton("Salvar", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            var defaults = new ThemedButton("Padrão da norma", false);
            footer.AddButton(_ok);
            footer.AddButton(cancel);
            footer.AddButton(defaults);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            Fill(current);

            _height.Box.Input.TextChanged += (s, e) => UpdateStatus();
            _clearance.Box.Input.TextChanged += (s, e) => { SyncPresets(); UpdateStatus(); };
            _method.SelectedChanged += (s, e) => { UpdateMethodHint(); UpdateStatus(); };
            _presets.SelectedChanged += (s, e) =>
            {
                if (_syncing || _presets.SelectedIndex < 0) return;
                _clearance.Value = Format(Clearances[_presets.SelectedIndex], "0.0");
            };
            defaults.Click += (s, e) => Fill(new CalcSettings());
            _ok.Click += (s, e) =>
            {
                if (Result == null) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (s, e) => _height.Box.Input.Focus();
        }

        private void Fill(CalcSettings settings)
        {
            _height.Value = Format(settings.AttachHeightM, "0.00");
            _method.SelectedIndex = settings.UseNormTable ? 0 : 1;
            _clearance.Value = Format(settings.MinGroundClearanceM, "0.0");
            SyncPresets();
            UpdateMethodHint();
            UpdateStatus();
        }

        /// <summary>Marca o atalho da Tabela 02 que corresponde ao valor digitado (nenhum se for outro valor).</summary>
        private void SyncPresets()
        {
            _syncing = true;
            double? value = Coordinates.ParseMeters(_clearance.Value);
            _presets.SelectedIndex = value == null ? -1 : Array.FindIndex(Clearances, c => Math.Abs(c - value.Value) < 0.001);
            _syncing = false;
        }

        private void UpdateMethodHint() =>
            _methodHint.Text = _method.SelectedIndex == 0
                ? "Tração pela Tabela 08 da norma (cabo autossustentado, flecha de 1%), pelo número de fibras e o comprimento do vão."
                : "T = p·L² / (8·f), com flecha f de 1% do vão: T = 12,5 · p · L (p em kgf/m), pela coluna Peso da planilha de cabos.";

        private void UpdateStatus()
        {
            Result = null;
            double? height = Coordinates.ParseMeters(_height.Value);
            double? clearance = Coordinates.ParseMeters(_clearance.Value);

            if (height == null || height < 1 || height > 30)
            {
                ShowStatus("Informe a altura de fixação em metros, ex.: 5.40.", error: true);
            }
            else if (clearance == null || clearance <= 0 || clearance > 30)
            {
                ShowStatus("Informe a altura mínima ao solo em metros, ex.: 5.0.", error: true);
            }
            else
            {
                Result = new CalcSettings { AttachHeightM = height.Value, UseNormTable = _method.SelectedIndex == 0, MinGroundClearanceM = clearance.Value };
                string summary = $"Cabo a {Format(height.Value, "0.00")} m · tração {(Result.UseNormTable ? "pela Tabela 08" : "pelo peso do cabo")} · " +
                                 $"mínimo de {Format(clearance.Value, "0.0")} m ao solo";
                if (height < CalcSettings.MinAttachHeight || height > CalcSettings.MaxAttachHeight)
                    ShowStatus(summary + $"  ·  fora da faixa da NDU 009 ({Format(CalcSettings.MinAttachHeight, "0.00")} a {Format(CalcSettings.MaxAttachHeight, "0.00")} m)", error: true);
                else if (clearance >= height)
                    ShowStatus(summary + "  ·  o mínimo ao solo é maior que a altura de fixação: todos os vãos ficarão abaixo", error: true);
                else
                    ShowStatus(summary, error: false);
            }
            _ok.Enabled = Result != null;
        }

        private void ShowStatus(string text, bool error)
        {
            _status.Text = text;
            _status.ForeColor = error ? (Theme.Current.IsDark ? Color.FromArgb(0xEE, 0x5A, 0x43) : Color.FromArgb(0xD2, 0x3B, 0x24)) : Theme.Text;
        }

        /// <summary>Número com vírgula, como no resto da janela ("5,40").</summary>
        private static string Format(double value, string format) => value.ToString(format, CultureInfo.GetCultureInfo("pt-BR"));

        private static Label Section(string text) => new Label
        {
            Text = text.ToUpperInvariant(),
            Font = Theme.Section,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = Padding.Empty
        };

        private static Label Hint(string text) => new Label
        {
            Text = text,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = false,
            Margin = new Padding(0, 2, 0, 0)
        };
    }
}
