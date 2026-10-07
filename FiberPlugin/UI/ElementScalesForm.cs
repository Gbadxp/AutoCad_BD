using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Escala de cada elemento do desenho (botão Por elemento da aba Projeto): ícone e texto dos postes e das CTO/CEO,
    /// nome e metragem dos cabos e setas de esforço. Campo vazio = escala do desenho. A linha Todos preenche a coluna
    /// inteira de uma vez (todos os ícones ou todos os textos).
    /// </summary>
    internal class ElementScalesForm : Form
    {
        private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

        private readonly int _drawing;
        private readonly Dictionary<ScaleItem, InputBox> _fields = new Dictionary<ScaleItem, InputBox>();
        private readonly InputBox _allIcons, _allTexts;
        private readonly StatusLabel _status;
        private readonly ThemedButton _ok;

        private static readonly ScaleItem[] Icons = { ScaleItem.PoleIcon, ScaleItem.BoxIcon, ScaleItem.Effort };
        private static readonly ScaleItem[] Texts = { ScaleItem.PoleText, ScaleItem.BoxText, ScaleItem.CableText };

        /// <summary>Escalas escolhidas (valem depois do OK).</summary>
        public ElementScales Result { get; private set; }

        /// <param name="drawing">Escala do desenho (1:X), mostrada nos campos vazios.</param>
        public ElementScalesForm(ElementScales current, int drawing)
        {
            _drawing = drawing;
            Result = current.Clone();
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Escala por elemento";
            ClientSize = new Size(600, 440);

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_CONFIGURACOES",
                Title = "Escala por elemento",
                Subtitle = $"Ícone e texto de cada elemento; vazio = escala do desenho (1:{drawing.ToString("N0", Br)})"
            };

            string placeholder = drawing.ToString(CultureInfo.InvariantCulture);
            InputBox Field(ScaleItem item)
            {
                var box = new InputBox(placeholder, searchIcon: false);
                if (current[item] > 0) box.Input.Text = current[item].ToString(CultureInfo.InvariantCulture);
                box.Input.TextChanged += (s, e) => Check();
                _fields[item] = box;
                return box;
            }
            _allIcons = new InputBox("todos", searchIcon: false);
            _allTexts = new InputBox("todos", searchIcon: false);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Theme.Background, Padding = new Padding(18, 8, 18, 4) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));

            Label RowLabel(string text, bool strong = false) => new Label
            {
                Text = text,
                ForeColor = strong ? Theme.TextStrong : Theme.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            Label None() => new Label { Text = "—", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter };

            var rows = new (Control Name, Control Icon, Control Text, float Height)[]
            {
                (new SectionLabel("Elemento"), new SectionLabel("Ícone   1:"), new SectionLabel("Texto   1:"), 26),
                (RowLabel("Todos (preenche a coluna)", strong: true), _allIcons, _allTexts, 38),
                (RowLabel("Postes"), Field(ScaleItem.PoleIcon), Field(ScaleItem.PoleText), 38),
                (RowLabel("CTO e CEO"), Field(ScaleItem.BoxIcon), Field(ScaleItem.BoxText), 38),
                (RowLabel("Nome e metragem dos cabos"), None(), Field(ScaleItem.CableText), 38),
                (RowLabel("Setas de esforço (seta e textos)"), Field(ScaleItem.Effort), None(), 38)
            };
            foreach (var (name, icon, text, height) in rows)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                int row = grid.RowCount++;
                foreach (var (control, column) in new[] { (name, 0), (icon, 1), (text, 2) })
                {
                    control.Dock = DockStyle.Fill;
                    control.Margin = new Padding(column == 0 ? 0 : 6, 4, 0, 4);
                    grid.Controls.Add(control, column, row);
                }
            }

            Label hint = FormLayout.Hint("O bloco do poste vem no tamanho do BLOCOS.dwg (1:1.000); apague o valor para ele seguir a escala " +
                                         "do desenho. Ao salvar as Configurações, o plugin pergunta se ajusta o que já está desenhado.");
            _status = new StatusLabel();
            foreach (var (control, height) in new (Control, float)[] { (hint, 62), (_status, 30) })
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                control.Dock = DockStyle.Fill;
                grid.Controls.Add(control, 0, grid.RowCount++);
                grid.SetColumnSpan(control, 3);
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowCount++;

            var footer = new FooterPanel();
            _ok = new ThemedButton("OK", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            var defaults = new ThemedButton("Padrão", false);
            footer.AddButton(_ok);
            footer.AddButton(cancel);
            footer.AddButton(defaults);

            Controls.Add(grid);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            // Todos: o que for digitado vai para a coluna inteira
            _allIcons.Input.TextChanged += (s, e) => FillColumn(Icons, _allIcons.Query);
            _allTexts.Input.TextChanged += (s, e) => FillColumn(Texts, _allTexts.Query);
            defaults.Click += (s, e) =>
            {
                var standard = new ElementScales();
                foreach (var pair in _fields) pair.Value.Input.Text = standard[pair.Key] > 0 ? standard[pair.Key].ToString(CultureInfo.InvariantCulture) : "";
            };
            _ok.Click += (s, e) =>
            {
                if (Check()) { DialogResult = DialogResult.OK; Close(); }
            };
            Check();
        }

        private void FillColumn(ScaleItem[] items, string text)
        {
            if (text.Length > 0 && Parse(text) == null) return; // Só depois de virar um número válido
            foreach (ScaleItem item in items) _fields[item].Input.Text = text;
        }

        /// <summary>"2000", "1:2000", "2.000" ou "2 000" → 2000. Null se não for um número.</summary>
        internal static int? Parse(string text)
        {
            string t = text.Trim();
            if (t.StartsWith("1:")) t = t.Substring(2);
            t = t.Replace(".", "").Replace(",", "").Replace(" ", "");
            return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : (int?)null;
        }

        /// <summary>Confere os campos e monta o resultado. False (com a mensagem na linha de status) se algum estiver errado.</summary>
        private bool Check()
        {
            var result = new ElementScales();
            foreach (var pair in _fields)
            {
                string text = pair.Value.Query;
                if (text.Length == 0) { result[pair.Key] = 0; continue; }

                int? value = Parse(text);
                if (value == null || value < DrawingScale.Min || value > DrawingScale.Max)
                {
                    string label = ElementScales.Label(pair.Key);
                    _status.Set($"{char.ToUpper(label[0]) + label.Substring(1)}: use 1:X com X de {DrawingScale.Min} a {DrawingScale.Max.ToString("N0", Br)} (ex.: 2000).",
                        StatusKind.Error);
                    _ok.Enabled = false;
                    return false;
                }
                result[pair.Key] = value.Value;
            }

            Result = result;
            string own = result.Summary(_drawing);
            _status.Set(own.Length == 0 ? $"Tudo na escala do desenho (1:{_drawing.ToString("N0", Br)})." : "Com escala própria: " + own + ".", StatusKind.Ok);
            _ok.Enabled = true;
            return true;
        }
    }
}
