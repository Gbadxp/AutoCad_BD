using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Janela Visibilidade: marca os tipos de objeto que ficam à vista e o filtro do cadastro na Energisa, mostra quantos
    /// ficam ocultos e guarda as combinações como presets (Documentos\Fiber Plugin\visibilidade.txt).
    /// </summary>
    internal class VisibilityForm : Form
    {
        private static readonly Dictionary<string, string> CheckLabels = new Dictionary<string, string>
        {
            [Registration.Poles] = "Postes (com o texto)",
            [Registration.Cto] = "CTO (com o texto)",
            [Registration.Ceo] = "CEO (com o texto)",
            [Registration.Cables] = "Cabos (com os textos dos vãos)",
            [VisibilityFilter.Arrows] = "Setas de esforço",
            [Registration.Others] = "Outros: elétricos, amarrações, cordoalha e reserva"
        };

        private readonly IReadOnlyList<(string Kind, bool? Done)> _objects;
        private readonly List<VisibilityPreset> _presets;
        private readonly ListBox _list;
        private readonly LabeledInput _name;
        private readonly Dictionary<string, CheckBox> _checks = new Dictionary<string, CheckBox>();
        private readonly ChoiceBar _status;
        private readonly StatusLabel _summary;
        private readonly ThemedButton _ok, _delete, _save;
        private bool _filling;

        /// <summary>O filtro escolhido.</summary>
        public VisibilityFilter Result { get; private set; } = new VisibilityFilter();

        /// <summary>Botão Mostrar tudo.</summary>
        public bool ShowAll { get; private set; }

        /// <param name="objects">Objetos do projeto: tipo e se está cadastrado (null nas setas sem poste).</param>
        /// <param name="current">Filtro aplicado por último neste desenho.</param>
        public VisibilityForm(IReadOnlyList<(string Kind, bool? Done)> objects, VisibilityFilter current, List<VisibilityPreset> presets)
        {
            _objects = objects;
            _presets = presets;

            Theme.ApplyForm(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Fiber Plugin - Visibilidade";
            ClientSize = new Size(S(820), S(470));

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_VISIBILIDADE",
                Title = "Visibilidade",
                Subtitle = "Oculta objetos do projeto (na tela, nas folhas e na impressão) por tipo e pelo cadastro na Energisa"
            };

            // ---------- Presets ----------
            _list = new ListBox
            {
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                ItemHeight = S(20),
                Margin = new Padding(0, S(2), 0, S(4))
            };
            Theme.UseDarkScrollbars(_list);
            _name = new LabeledInput("Nome do preset", "ex.: Cadastro bairro Centro");
            _save = new ThemedButton("Salvar preset", false) { Margin = new Padding(0, 0, S(6), 0) };
            _delete = new ThemedButton("Apagar preset", false) { Margin = Padding.Empty };
            var presetButtons = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
            presetButtons.Controls.AddRange(new Control[] { _save, _delete });
            var left = Column((new SectionLabel("Presets"), 26), (_list, 0), (_name, 56), (presetButtons, 36));

            // ---------- O que aparece ----------
            var checks = new TableLayoutPanel { ColumnCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
            checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (string kind in VisibilityFilter.Kinds)
            {
                var check = new CheckBox { Text = CheckLabels[kind], ForeColor = Theme.Text, BackColor = Theme.Background, Font = Theme.Body, AutoSize = true, Margin = new Padding(0, S(2), 0, S(2)) };
                _checks[kind] = check;
                checks.RowStyles.Add(new RowStyle(SizeType.Absolute, S(25)));
                checks.Controls.Add(check, 0, checks.RowCount++);
            }
            _status = new ChoiceBar("Todos", "Só os cadastrados", "Só os que faltam");
            var hint = FormLayout.Hint("Oculto some da tela, das folhas e da impressão, mas continua no desenho: relatório, esforço e memorial " +
                                       "contam com ele. Objeto novo entra à vista: aplique o preset de novo depois de desenhar.");
            _summary = new StatusLabel { Margin = Padding.Empty };
            var right = Column((new SectionLabel("O que fica à vista"), 26), (checks, 156),
                (new SectionLabel("Cadastro na Energisa"), 30), (_status, 38), (hint, 52), (new Panel { BackColor = Theme.Background }, 0), (_summary, 40));
            right.Padding = new Padding(S(16), 0, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(S(18), S(6), S(18), S(8)), BackColor = Theme.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(left, 0, 0);
            body.Controls.Add(right, 1, 0);

            var footer = new FooterPanel { Hint = "Os presets valem para todos os desenhos" };
            _ok = new ThemedButton("Aplicar", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            var showAll = new ThemedButton("Mostrar tudo", false);
            footer.AddButton(_ok);
            footer.AddButton(cancel);
            footer.AddButton(showAll);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            // ---------- Valores iniciais ----------
            FillList(null);
            Fill(current);
            VisibilityPreset? same = _presets.FirstOrDefault(p => p.Filter.Code == current.Code);
            if (same != null)
            {
                _filling = true;
                _list.SelectedItem = same;
                _name.Value = same.Name;
                _filling = false;
            }

            // ---------- Eventos ----------
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_filling || _list.SelectedItem is not VisibilityPreset preset) return;
                _name.Value = preset.Name;
                Fill(preset.Filter);
            };
            _list.DoubleClick += (s, e) => { if (_list.SelectedItem is VisibilityPreset) Apply(); };
            foreach (CheckBox check in _checks.Values) check.CheckedChanged += (s, e) => UpdateAll();
            _status.SelectedChanged += (s, e) => UpdateAll();
            _name.Box.Input.TextChanged += (s, e) => UpdateAll();
            _save.Click += (s, e) => SavePreset();
            _delete.Click += (s, e) => DeletePreset();
            _ok.Click += (s, e) => Apply();
            showAll.Click += (s, e) =>
            {
                ShowAll = true;
                Result = new VisibilityFilter();
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (s, e) => _ok.Focus();

            UpdateAll();
        }

        /// <summary>Pixels de 96 dpi na escala da tela.</summary>
        private int S(int pixels) => Theme.Scale(this, pixels);

        private void Apply()
        {
            Result = Current();
            DialogResult = DialogResult.OK;
            Close();
        }

        private VisibilityFilter Current() => new VisibilityFilter
        {
            Shown = new HashSet<string>(_checks.Where(c => c.Value.Checked).Select(c => c.Key), StringComparer.OrdinalIgnoreCase),
            Status = Math.Max(0, _status.SelectedIndex)
        };

        private void Fill(VisibilityFilter filter)
        {
            _filling = true;
            foreach (var pair in _checks) pair.Value.Checked = filter.Shown.Contains(pair.Key);
            _status.SelectedIndex = filter.Status;
            _filling = false;
            UpdateAll();
        }

        private void FillList(VisibilityPreset? select)
        {
            _filling = true;
            _list.Items.Clear();
            foreach (VisibilityPreset p in _presets) _list.Items.Add(p);
            _list.SelectedItem = select;
            _filling = false;
        }

        private void SavePreset()
        {
            string name = _name.Value.Trim();
            if (name.Length == 0 || name.Contains("=")) return;
            VisibilityPreset? existing = _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null && MessageBox.Show(this, $"Trocar o preset \"{existing.Name}\" pelo que está marcado agora?", "Fiber Plugin - Visibilidade",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var preset = existing ?? new VisibilityPreset { Name = name };
            preset.Filter = Current();
            if (existing == null) _presets.Add(preset);
            Store(preset);
        }

        private void DeletePreset()
        {
            if (_list.SelectedItem is not VisibilityPreset preset) return;
            if (MessageBox.Show(this, $"Apagar o preset \"{preset.Name}\"?", "Fiber Plugin - Visibilidade",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _presets.Remove(preset);
            Store(null);
        }

        private void Store(VisibilityPreset? select)
        {
            FillList(select);
            if (VisibilityPresets.Save(_presets) is string error)
                _summary.Set($"Não foi possível gravar os presets ({error}).", StatusKind.Error);
            else
                UpdateAll();
        }

        private void UpdateAll()
        {
            if (_filling) return;
            _delete.Enabled = _list.SelectedItem is VisibilityPreset;
            string name = _name.Value.Trim();
            _save.Enabled = name.Length > 0 && !name.Contains("=");
            VisibilityFilter filter = Current();
            var hidden = _objects.Where(o => !filter.Shows(o.Kind, o.Done)).GroupBy(o => o.Kind)
                .OrderBy(g => Array.IndexOf(VisibilityFilter.Kinds, g.Key)).Select(g => $"{VisibilityFilter.Label(g.Key)} {g.Count()}").ToList();
            int hiddenCount = _objects.Count(o => !filter.Shows(o.Kind, o.Done));
            string summary = _objects.Count == 0
                ? "O desenho não tem objetos do plugin."
                : hiddenCount == 0
                    ? $"Tudo à vista ({_objects.Count} objetos)."
                    : $"Ficam ocultos {hiddenCount} de {_objects.Count}: {string.Join(", ", hidden)}.";
            _summary.Set(summary, hiddenCount == 0 ? StatusKind.Ok : StatusKind.Hint);
        }

        /// <summary>Coluna de linhas com altura fixa (0 = ocupa o resto).</summary>
        private TableLayoutPanel Column(params (Control Control, int Height)[] rows)
        {
            var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Background };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (control, height) in rows)
            {
                column.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, S(height)) : new RowStyle(SizeType.Percent, 100));
                control.Dock = DockStyle.Fill;
                column.Controls.Add(control, 0, column.RowCount++);
            }
            return column;
        }
    }
}
