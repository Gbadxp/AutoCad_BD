using FiberPlugin.Core;
using FiberPlugin.Models;

namespace FiberPlugin.UI
{
    /// <summary>Tabela no estilo das listas do AutoCAD, para editar as planilhas da pasta Dados.</summary>
    internal class ThemedGrid : DataGridView
    {
        public ThemedGrid()
        {
            BorderStyle = BorderStyle.None;
            BackgroundColor = Theme.Surface;
            GridColor = Theme.Separator;
            EnableHeadersVisualStyles = false;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Header,
                ForeColor = Theme.Muted,
                SelectionBackColor = Theme.Header,
                SelectionForeColor = Theme.Muted,
                Font = Theme.Section,
                Padding = new Padding(4, 0, 4, 0)
            };
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                SelectionBackColor = Theme.Selection,
                SelectionForeColor = Theme.TextStrong,
                Font = Theme.Body,
                Padding = new Padding(4, 0, 4, 0)
            };
            RowHeadersVisible = false;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToOrderColumns = false;
            MultiSelect = false;
            SelectionMode = DataGridViewSelectionMode.CellSelect;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            // As barras de rolagem da tabela são controles filhos: o tema escuro vai em cada uma
            Theme.UseDarkScrollbars(VerticalScrollBar);
            Theme.UseDarkScrollbars(HorizontalScrollBar);
            DataError += (s, e) => e.ThrowException = false; // Valor fora da lista no ComboBox: sem caixa de erro

            // Campos de edição com as cores do tema (o ComboBox do Windows fica claro no tema escuro)
            EditingControlShowing += (s, e) =>
            {
                e.Control.BackColor = Theme.InputFocused;
                e.Control.ForeColor = Theme.Text;
                if (e.Control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ColumnHeadersHeight = Theme.Scale(this, 28);
            RowTemplate.Height = Theme.Scale(this, 26);
            foreach (DataGridViewRow row in Rows) row.Height = RowTemplate.Height;
        }

        public DataGridViewTextBoxColumn AddText(string header, float weight, bool numeric = false)
        {
            var column = new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (numeric) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            Columns.Add(column);
            return column;
        }

        /// <summary>Texto da célula sem espaços nas pontas ("" se vazia).</summary>
        public string CellText(int row, int column) => (Rows[row].Cells[column].Value as string ?? Rows[row].Cells[column].Value?.ToString() ?? "").Trim();

        /// <summary>Linha nova no fim, já editando a primeira célula.</summary>
        public void AddRowAndEdit(params object?[] values)
        {
            int index = Rows.Add(values);
            Rows[index].Height = RowTemplate.Height;
            CurrentCell = Rows[index].Cells[0];
            Focus();
            BeginEdit(true);
        }

        public void RemoveCurrentRow()
        {
            if (CurrentCell == null || Rows.Count == 0) return;
            int index = CurrentCell.RowIndex;
            EndEdit();
            Rows.RemoveAt(index);
            if (Rows.Count > 0) CurrentCell = Rows[Math.Min(index, Rows.Count - 1)].Cells[0];
        }
    }

    /// <summary>
    /// Janela Configurações (FIBRA_CONFIGURACOES): o que o plugin coloca sozinho no desenho, em quatro abas.
    /// Cabos e Postes editam as planilhas cabos.csv e postes.csv da pasta Dados (com a cor de cada cabo);
    /// Nomes e Tamanhos e cores vão para Documentos\Fiber Plugin\configuracoes.txt (UserSettings). Valem para
    /// todos os desenhos. Confere tudo enquanto o usuário digita.
    /// </summary>
    internal class SettingsForm : Form
    {
        private const int CableFull = 0, CableShort = 1, CableWeight = 2, CableFibers = 3, CableDiameter = 4, CableColor = 5;
        private const int PoleType = 0, PoleHeight = 1, PoleEffort = 2;
        private static readonly string[] TabNames = { "Cabos", "Postes", "Nomes", "Tamanhos e cores" };

        private readonly ChoiceBar _tabs;
        private readonly Control[] _pages;
        private readonly ThemedGrid _cables;
        private readonly ThemedGrid _poles;
        private readonly LabeledInput _polePrefix, _ctoPrefix, _ceoPrefix;
        private readonly ChoiceBar _digits;
        private readonly Label _example;
        private readonly LabeledInput _textHeight, _boxSize, _arrowLength, _routeOffset;
        private readonly ColorField _cableColor, _poleLabelColor, _boxLabelColor, _effortColor;
        private readonly StatusLabel _status;
        private readonly ThemedButton _ok, _defaults;

        /// <summary>Resultado (preenchidos enquanto tudo estiver certo).</summary>
        public List<CableModel> Cables { get; private set; } = new List<CableModel>();
        public List<PoleData> PoleTypes { get; private set; } = new List<PoleData>();
        public UserSettings Settings { get; private set; }

        /// <summary>Grava as alterações; retorna o erro (a janela continua aberta para tentar de novo) ou null.</summary>
        public Func<SettingsForm, string?>? SaveChanges { get; set; }

        /// <param name="cableNotes">Aviso da leitura do cabos.csv (linhas ignoradas, planilha ausente), mostrado na aba Cabos.</param>
        /// <param name="poleNotes">O mesmo para o postes.csv, na aba Postes.</param>
        public SettingsForm(List<CableModel> cables, List<PoleData> poles, UserSettings settings, string? cableNotes = null, string? poleNotes = null)
        {
            Settings = settings.Clone();
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Configurações";
            ClientSize = new Size(780, 640);

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_CONFIGURACOES",
                Title = "Configurações",
                Subtitle = "O que o plugin coloca sozinho no desenho · vale para todos os projetos"
            };

            _tabs = new ChoiceBar(TabNames);
            var tabBar = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(18, 10, 18, 4), BackColor = Theme.Background };
            tabBar.Controls.Add(_tabs);

            // ---------- Cabos ----------
            _cables = new ThemedGrid();
            _cables.AddText("Nome completo", 34);
            _cables.AddText("Nome curto", 19);
            _cables.AddText("Peso kg/km", 13, numeric: true);
            _cables.AddText("Fibras", 8, numeric: true);
            _cables.AddText("Diâm. mm", 11, numeric: true);
            _cables.AddText("Cor", 16).ReadOnly = true;
            foreach (CableModel c in cables)
            {
                _cables.Rows.Add(c.FullName, c.ShortName, Num(c.WeightKgKm), c.Fibers?.ToString() ?? "",
                    c.DiameterMm is double d ? Num(d) : "", c.Color);
            }
            _cables.CellPainting += PaintColorCell;
            _cables.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == CableColor) PickCableColor(e.RowIndex); };
            _cables.KeyDown += (s, e) =>
            {
                if (_cables.CurrentCell?.ColumnIndex != CableColor) return;
                if (e.KeyCode is Keys.Enter or Keys.Space) { PickCableColor(_cables.CurrentCell.RowIndex); e.Handled = true; }
                else if (e.KeyCode is Keys.Delete or Keys.Back) { _cables.CurrentCell.Value = null; e.Handled = true; }
            };

            var addCable = new ThemedButton("Adicionar cabo", false);
            var removeCable = new ThemedButton("Remover cabo", false);
            addCable.Click += (s, e) => _cables.AddRowAndEdit("", "", "", "", "", null);
            removeCable.Click += (s, e) => _cables.RemoveCurrentRow();
            Control cablesPage = GridPage(_cables, new[] { addCable, removeCable },
                "Clique na cor para trocar; Delete volta para a cor padrão (aba Tamanhos e cores). As layers dos cabos já lançados " +
                "mudam de cor ao salvar. Trocar o nome curto de um cabo já lançado faz o desenho perder o vínculo com a planilha.",
                cableNotes);

            // ---------- Postes ----------
            _poles = new ThemedGrid();
            var type = new DataGridViewComboBoxColumn
            {
                HeaderText = "Tipo",
                FillWeight = 30,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            type.Items.AddRange(PoleData.DoubleT, PoleData.Circular);
            _poles.Columns.Add(type);
            _poles.AddText("Altura (m)", 35, numeric: true);
            _poles.AddText("Esforço nominal (daN)", 35, numeric: true);
            foreach (PoleData p in poles) _poles.Rows.Add(p.Type, Num(p.HeightM), Num(p.EffortDaN));

            var addPole = new ThemedButton("Adicionar modelo", false);
            var removePole = new ThemedButton("Remover modelo", false);
            addPole.Click += (s, e) => _poles.AddRowAndEdit(PoleData.DoubleT, "", "");
            removePole.Click += (s, e) => _poles.RemoveCurrentRow();
            Control polesPage = GridPage(_poles, new[] { addPole, removePole },
                "Modelos oferecidos no Inserir Postes. DT = Duplo T, CC = Circular. Altura em metros e esforço em daN, como na norma " +
                "(o desenho mostra 11/300).", poleNotes);

            // ---------- Nomes ----------
            _polePrefix = new LabeledInput("Prefixo dos postes", "ex.: P-");
            _ctoPrefix = new LabeledInput("Prefixo das CTO", "ex.: CTO-");
            _ceoPrefix = new LabeledInput("Prefixo das CEO", "ex.: CEO-");
            _digits = new ChoiceBar("1 dígito", "2 dígitos", "3 dígitos", "4 dígitos");
            _example = new Label { Font = Theme.Title, ForeColor = Theme.TextStrong, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            Control namesPage = FormPage(new (Control, float)[]
            {
                (new SectionLabel("Prefixos"), 26),
                (Row(_polePrefix, _ctoPrefix, _ceoPrefix), 56),
                (Hint("Sem números: o número entra depois do prefixo. Pode deixar o dos postes vazio (01, 02...)."), 26),
                (new SectionLabel("Número"), 30), (_digits, 38),
                (new SectionLabel("Como fica"), 30), (_example, 40),
                (Hint("Vale para os próximos postes, CTO e CEO, e para o relatório, o memorial e o KML. Ao salvar, o plugin " +
                      "pergunta na linha de comando se atualiza os nomes já desenhados neste desenho (texto e atributo do bloco)."), 44)
            });

            // ---------- Tamanhos e cores ----------
            _textHeight = new LabeledInput("Altura dos textos (mm)", "ex.: 2.0");
            _boxSize = new LabeledInput("Símbolo da CTO/CEO (mm)", "ex.: 7.0");
            _arrowLength = new LabeledInput("Seta de esforço (mm)", "ex.: 18");
            _routeOffset = new LabeledInput("Cabo afastado do poste (m)", "ex.: 1.8");
            _cableColor = new ColorField("Cabos sem cor própria");
            _poleLabelColor = new ColorField("Textos dos postes");
            _boxLabelColor = new ColorField("Textos das CTO/CEO");
            _effortColor = new ColorField("Setas de esforço");
            Control sizesPage = FormPage(new (Control, float)[]
            {
                (new SectionLabel("Tamanhos no papel, na escala 1:1000"), 26),
                (Row(_textHeight, _boxSize, _arrowLength), 56),
                (Hint("Em outra escala (Dados do Projeto) os tamanhos acompanham: em 1:2000 ficam o dobro no desenho. " +
                      "Ao salvar, o plugin pergunta se ajusta os textos já desenhados; símbolos e setas mudam nos próximos."), 40),
                (new SectionLabel("Roteamento automático"), 30),
                (Row(_routeOffset, new Panel { BackColor = Theme.Background }, new Panel { BackColor = Theme.Background }), 56),
                (new SectionLabel("Cores das layers"), 30),
                (Row(_cableColor, _poleLabelColor), 56),
                (Row(_boxLabelColor, _effortColor), 56),
                (Hint("As layers que já existem neste desenho mudam de cor ao salvar."), 24)
            });

            _pages = new[] { cablesPage, polesPage, namesPage, sizesPage };
            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(18, 4, 18, 0) };
            foreach (Control page in _pages)
            {
                page.Dock = DockStyle.Fill;
                pages.Controls.Add(page);
            }

            _status = new StatusLabel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(18, 0, 18, 0) };

            var footer = new FooterPanel { Hint = "Cabos e postes: pasta Dados · o resto: configuracoes.txt" };
            _ok = new ThemedButton("Salvar", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            _defaults = new ThemedButton("Restaurar padrão", false);
            footer.AddButton(_ok);
            footer.AddButton(cancel);
            footer.AddButton(_defaults);

            Controls.Add(pages);
            Controls.Add(_status);
            Controls.Add(tabBar);
            Controls.Add(header);
            Controls.Add(footer);
            CancelButton = cancel;

            Fill(settings);

            foreach (LabeledInput input in new[] { _polePrefix, _ctoPrefix, _ceoPrefix, _textHeight, _boxSize, _arrowLength, _routeOffset })
            {
                input.Box.Input.TextChanged += (s, e) => UpdateStatus();
            }
            foreach (ColorField field in new[] { _cableColor, _poleLabelColor, _boxLabelColor, _effortColor })
            {
                field.ValueChanged += (s, e) => { UpdateStatus(); _cables.Invalidate(); };
            }
            _digits.SelectedChanged += (s, e) => UpdateStatus();
            foreach (ThemedGrid grid in new[] { _cables, _poles })
            {
                grid.CellValueChanged += (s, e) => UpdateStatus();
                grid.RowsRemoved += (s, e) => UpdateStatus();
                grid.RowsAdded += (s, e) => UpdateStatus();
                // O ComboBox do tipo grava na hora, sem esperar sair da célula
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            }
            _tabs.SelectedChanged += (s, e) => ShowPage(_tabs.SelectedIndex);
            _defaults.Click += (s, e) => RestoreDefaults();
            _ok.Click += (s, e) => Save();

            // As páginas ficam visíveis até a janela carregar: escondidas antes disso não acompanham a escala do DPI
            Load += (s, e) =>
            {
                ShowPage(_tabs.SelectedIndex);
                _cables.ClearSelection();
                _poles.ClearSelection();
            };
            UpdateStatus();
        }

        private void ShowPage(int index)
        {
            if (index < 0) return;
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == index;
            _pages[index].BringToFront();
            _defaults.Visible = index >= 2; // Cabos e postes não têm "padrão": são as planilhas do usuário
        }

        private void Fill(UserSettings s)
        {
            _polePrefix.Value = s.PolePrefix;
            _ctoPrefix.Value = s.CtoPrefix;
            _ceoPrefix.Value = s.CeoPrefix;
            _digits.SelectedIndex = s.NumberDigits - 1;
            FillSizes(s);
        }

        private void FillSizes(UserSettings s)
        {
            _textHeight.Value = Num(s.TextHeight);
            _boxSize.Value = Num(s.BoxSymbolSize);
            _arrowLength.Value = Num(s.EffortArrowLength);
            _routeOffset.Value = Num(s.AutoRouteOffset);
            _cableColor.Value = s.CableColor;
            _poleLabelColor.Value = s.PoleLabelColor;
            _boxLabelColor.Value = s.BoxLabelColor;
            _effortColor.Value = s.EffortColor;
        }

        private void RestoreDefaults()
        {
            var defaults = new UserSettings();
            if (_tabs.SelectedIndex == 2)
            {
                _polePrefix.Value = defaults.PolePrefix;
                _ctoPrefix.Value = defaults.CtoPrefix;
                _ceoPrefix.Value = defaults.CeoPrefix;
                _digits.SelectedIndex = defaults.NumberDigits - 1;
            }
            else
            {
                FillSizes(defaults);
            }
        }

        private void PickCableColor(int row)
        {
            DataGridViewCell cell = _cables.Rows[row].Cells[CableColor];
            string name = _cables.CellText(row, CableShort);
            if (ColorPickerForm.Pick(this, name.Length > 0 ? "Cor do cabo " + name : "Cor do cabo", cell.Value as short?, _cableColor.Value, out short? color))
            {
                cell.Value = color;
            }
        }

        /// <summary>Célula de cor: amostra e nome; sem cor própria, a cor padrão dos cabos em cinza.</summary>
        private void PaintColorCell(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != CableColor || e.Graphics == null) return;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);

            short? own = e.Value as short?;
            short color = own ?? _cableColor.Value;
            int size = e.CellBounds.Height - Theme.Scale(_cables, 12);
            var swatch = new Rectangle(e.CellBounds.X + Theme.Scale(_cables, 6), e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size * 2, size);
            AciColors.DrawSwatch(e.Graphics, swatch, color, dimmed: own == null);

            var text = new Rectangle(swatch.Right + Theme.Scale(_cables, 6), e.CellBounds.Y, e.CellBounds.Right - swatch.Right - Theme.Scale(_cables, 8), e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, own == null ? "padrão" : AciColors.Label(color), Theme.Body, text,
                own == null ? Theme.Muted : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.Handled = true;
        }

        private void Save()
        {
            // Célula ainda em edição entra no que vai ser gravado
            _cables.EndEdit();
            _poles.EndEdit();
            UpdateStatus();
            if (!_ok.Enabled) return;
            string? error = SaveChanges?.Invoke(this);
            if (error != null)
            {
                _status.Set("Não foi possível salvar: " + error, StatusKind.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Confere as quatro abas e monta o resultado. O primeiro problema aparece na linha de status.</summary>
        private void UpdateStatus()
        {
            string? cableError = ReadCables(out List<CableModel> cables);
            string? poleError = ReadPoles(out List<PoleData> poles);
            string? settingsError = ReadSettings(out UserSettings settings);
            string? error = cableError ?? poleError ?? settingsError;
            if (error == null)
            {
                Cables = cables;
                PoleTypes = poles;
                Settings = settings;
                _example.Text = $"{Settings.Name(Settings.PolePrefix, 7)}    {Settings.Name(Settings.CtoPrefix, 3)}    {Settings.Name(Settings.CeoPrefix, 1)}";
                _status.Set($"{Cables.Count} cabo(s) · {PoleTypes.Count} modelo(s) de poste · {Settings.Name(Settings.PolePrefix, 1)}, " +
                            $"{Settings.Name(Settings.CtoPrefix, 1)}, {Settings.Name(Settings.CeoPrefix, 1)} · texto de {Num(Settings.TextHeight)} mm · " +
                            $"cabo a {Num(Settings.AutoRouteOffset)} m do poste", StatusKind.Ok);
            }
            else
            {
                _status.Set(error, StatusKind.Error);
            }
            _ok.Enabled = error == null;
        }

        private string? ReadCables(out List<CableModel> cables)
        {
            cables = new List<CableModel>();
            for (int i = 0; i < _cables.Rows.Count; i++)
            {
                string full = _cables.CellText(i, CableFull), shortName = _cables.CellText(i, CableShort), weightText = _cables.CellText(i, CableWeight);
                string fibersText = _cables.CellText(i, CableFibers), diameterText = _cables.CellText(i, CableDiameter);
                if (full.Length == 0 && shortName.Length == 0 && weightText.Length == 0 && fibersText.Length == 0 && diameterText.Length == 0) continue;

                string where = $"Cabos, linha {i + 1}";
                if (full.Length == 0) return $"{where}: falta o nome completo.";
                if (shortName.Length == 0) return $"{where}: falta o nome curto (o que aparece no desenho).";
                if ((full + shortName).IndexOfAny(new[] { ';', '"' }) >= 0) return $"{where}: os nomes não podem ter ; nem aspas.";
                if (cables.Any(c => c.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase))) return $"{where}: o nome curto '{shortName}' já existe.";
                if (!DataFiles.TryParseNumber(weightText, out double weight) || weight <= 0) return $"{where}: informe o peso em kg/km (ex.: 31).";

                int? fibers = null;
                if (fibersText.Length > 0)
                {
                    if (!int.TryParse(fibersText, out int f) || f <= 0) return $"{where}: fibras deve ser um número inteiro (ex.: 12).";
                    fibers = f;
                }
                double? diameter = null;
                if (diameterText.Length > 0)
                {
                    if (!DataFiles.TryParseNumber(diameterText, out double d) || d <= 0) return $"{where}: diâmetro em mm inválido (ex.: 9,5).";
                    diameter = d;
                }

                cables.Add(new CableModel
                {
                    FullName = full,
                    ShortName = shortName,
                    WeightKgKm = weight,
                    Fibers = fibers ?? CableModel.FibersFromName(shortName + " " + full),
                    DiameterMm = diameter,
                    Color = _cables.Rows[i].Cells[CableColor].Value as short?
                });
            }
            return null;
        }

        private string? ReadPoles(out List<PoleData> poles)
        {
            poles = new List<PoleData>();
            for (int i = 0; i < _poles.Rows.Count; i++)
            {
                string type = _poles.CellText(i, PoleType).ToUpperInvariant(), heightText = _poles.CellText(i, PoleHeight), effortText = _poles.CellText(i, PoleEffort);
                if (heightText.Length == 0 && effortText.Length == 0) continue;

                string where = $"Postes, linha {i + 1}";
                if (type != PoleData.DoubleT && type != PoleData.Circular) return $"{where}: escolha o tipo DT ou CC.";
                if (!DataFiles.TryParseNumber(heightText, out double height) || height <= 0 || height > 40) return $"{where}: informe a altura em metros (ex.: 11).";
                if (!DataFiles.TryParseNumber(effortText, out double effort) || effort <= 0) return $"{where}: informe o esforço nominal em daN (ex.: 300).";
                poles.Add(new PoleData { Type = type, HeightM = height, EffortDaN = effort });
            }
            return null;
        }

        private string? ReadSettings(out UserSettings settings)
        {
            var s = Settings.Clone();
            settings = s;
            s.PolePrefix = _polePrefix.Value;
            s.CtoPrefix = _ctoPrefix.Value;
            s.CeoPrefix = _ceoPrefix.Value;
            s.NumberDigits = Math.Max(1, _digits.SelectedIndex + 1);

            foreach (var (input, apply) in new (LabeledInput, Action<double>)[]
            {
                (_textHeight, v => s.TextHeight = v),
                (_boxSize, v => s.BoxSymbolSize = v),
                (_arrowLength, v => s.EffortArrowLength = v),
                (_routeOffset, v => s.AutoRouteOffset = v)
            })
            {
                if (!DataFiles.TryParseNumber(input.Value, out double value)) return $"Tamanhos e cores: informe {input.Title.ToLowerInvariant()} (ex.: {input.Box.Placeholder.Replace("ex.: ", "")}).";
                apply(value);
            }

            s.CableColor = _cableColor.Value;
            s.PoleLabelColor = _poleLabelColor.Value;
            s.BoxLabelColor = _boxLabelColor.Value;
            s.EffortColor = _effortColor.Value;

            string? error = s.Validate();
            if (error == null) return null;
            bool names = error.StartsWith("Prefixo") || error.StartsWith("Os prefixos") || error.Contains("dígitos");
            return (names ? "Nomes: " : "Tamanhos e cores: ") + error;
        }

        // ---------- Montagem das páginas ----------

        /// <summary>Tabela, botões de adicionar/remover e dica embaixo (e o aviso da leitura, se houver).</summary>
        private static Control GridPage(ThemedGrid grid, ThemedButton[] buttons, string hint, string? notes)
        {
            var page = new TableLayoutPanel { ColumnCount = 1, BackColor = Theme.Background, Padding = new Padding(0, 4, 0, 0) };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border, Margin = Padding.Empty };
            grid.Dock = DockStyle.Fill;
            frame.Controls.Add(grid);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Background, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) };
            foreach (ThemedButton b in buttons)
            {
                b.Margin = new Padding(0, 0, 6, 0);
                bar.Controls.Add(b);
            }

            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(frame, 0, 0);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            page.Controls.Add(bar, 0, 1);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Label hintLabel = Hint(hint);
            hintLabel.Dock = DockStyle.Fill;
            page.Controls.Add(hintLabel, 0, 2);
            if (notes != null)
            {
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                Label notesLabel = Hint(notes);
                notesLabel.ForeColor = Theme.Error;
                notesLabel.Dock = DockStyle.Fill;
                page.Controls.Add(notesLabel, 0, 3);
            }
            page.RowCount = page.RowStyles.Count;
            return page;
        }

        /// <summary>Linhas de altura fixa, com a sobra numa linha vazia no fim (senão a última linha estica).</summary>
        private static Control FormPage((Control Control, float Height)[] rows)
        {
            var page = new TableLayoutPanel { ColumnCount = 1, BackColor = Theme.Background, Padding = new Padding(0, 4, 0, 0) };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (control, height) in rows)
            {
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                control.Dock = DockStyle.Fill;
                control.Margin = new Padding(0, 2, 0, 0);
                page.Controls.Add(control, 0, page.RowCount++);
            }
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.RowCount++;
            return page;
        }

        /// <summary>Campos lado a lado, com a mesma largura.</summary>
        private static Control Row(params Control[] controls)
        {
            var row = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < controls.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
                controls[i].Dock = DockStyle.Fill;
                controls[i].Margin = new Padding(i == 0 ? 0 : 6, 0, i == controls.Length - 1 ? 0 : 6, 0);
                row.Controls.Add(controls[i], i, 0);
            }
            return row;
        }

        private static Label Hint(string text) => new Label
        {
            Text = text,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = false,
            Margin = new Padding(0, 2, 0, 0)
        };

        /// <summary>Número com vírgula e sem zeros sobrando ("2", "1,8"), como no resto das janelas.</summary>
        private static string Num(double value) => value.ToString("0.###", DataFiles.Br);
    }
}
