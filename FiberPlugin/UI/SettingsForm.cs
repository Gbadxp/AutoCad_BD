using FiberPlugin.Core;
using FiberPlugin.Models;

namespace FiberPlugin.UI
{
    /// <summary>Tabela no estilo das listas do AutoCAD, para editar os cadastros do plugin.</summary>
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
            // O ComboBox grava na hora, sem esperar sair da célula
            CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (CurrentCell is DataGridViewComboBoxCell) CommitEdit(DataGridViewDataErrorContexts.Commit);
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

        /// <summary>Coluna de escolha numa lista; aparece como texto até a célula ser editada.</summary>
        public DataGridViewComboBoxColumn AddCombo(string header, float weight, IEnumerable<string> items)
        {
            var column = new DataGridViewComboBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MaxDropDownItems = 16
            };
            column.Items.AddRange(items.Cast<object>().ToArray());
            Columns.Add(column);
            return column;
        }

        /// <summary>Texto da célula sem espaços nas pontas ("" se vazia).</summary>
        public string CellText(int row, int column) => (Rows[row].Cells[column].Value as string ?? Rows[row].Cells[column].Value?.ToString() ?? "").Trim();

        /// <summary>Troca todas as linhas (valor fora da lista de uma coluna de escolha entra na lista).</summary>
        public void ReplaceRows(IEnumerable<object?[]> rows)
        {
            Rows.Clear();
            foreach (object?[] values in rows)
            {
                for (int c = 0; c < values.Length && c < Columns.Count; c++)
                {
                    if (Columns[c] is DataGridViewComboBoxColumn combo && values[c] is string text && !combo.Items.Contains(text)) combo.Items.Add(text);
                }
                int index = Rows.Add(values);
                if (IsHandleCreated) Rows[index].Height = RowTemplate.Height;
            }
            ClearSelection();
        }

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
    /// Janela Configurações (FIBRA_CONFIGURACOES): os dados do plugin e o que ele coloca sozinho no desenho, em seis
    /// abas. Cabos (com cor, tipo de linha e espessura), Postes, Tração (Tabela 08) e Empresa são os cadastros do plugin
    /// (Documentos\Fiber Plugin\cabos.txt, postes.txt, tracao.txt e empresa.txt); Nomes e Desenho vão para
    /// configuracoes.txt (UserSettings). Valem para todos os desenhos. Confere tudo enquanto o usuário digita.
    /// </summary>
    internal class SettingsForm : Form
    {
        private const int CableFull = 0, CableShort = 1, CableWeight = 2, CableFibers = 3, CableDiameter = 4, CableColor = 5,
            CableLinetype = 6, CableLineWeight = 7;
        private const int PoleType = 0, PoleHeight = 1, PoleEffort = 2;
        public const int TabProject = 0, TabCables = 1, TabPoles = 2, TabTraction = 3, TabCompany = 4, TabNames = 5, TabDrawing = 6, TabShortcuts = 7;
        private static readonly string[] TabTitles = { "Projeto", "Cabos", "Postes", "Tração", "Empresa", "Nomes", "Desenho", "Atalhos" };
        private const int ShortcutTitle = 0, ShortcutCommand = 1, ShortcutAlias = 2, ShortcutState = 3;
        private static readonly string[] LinetypeLabels = LayerStyle.Linetypes.Select(l => l.Label).ToArray();
        private static readonly string[] WeightLabels =
            new[] { LayerStyle.DefaultWeightLabel }.Concat(LayerStyle.Weights.Select(w => LayerStyle.WeightLabel(w))).ToArray();

        /// <summary>Campos da aba Empresa, por linha (o número é a largura relativa).</summary>
        private static readonly (string Section, (string Field, float Width)[][] Rows)[] CompanyLayout =
        {
            ("Empresa", new[]
            {
                new[] { ("Razão social", 2f), ("CNPJ", 1f) },
                new[] { ("Nome na plaqueta", 1f), ("Tipo de companhia", 1f), ("E-mail", 1f) },
                new[] { ("Endereço", 2f), ("CEP", 1f) },
                new[] { ("Cidade", 1f), ("Telefone", 1f), ("Telefone de emergência", 1f) }
            }),
            ("Representante legal", new[]
            {
                new[] { ("Representante", 1f), ("Qualificação", 1f), ("CREA", 1f) },
                new[] { ("RG", 1f), ("CPF", 1f), ("Endereço do representante", 1f) }
            }),
            ("Concessionária", new[]
            {
                new[] { ("Concessionária", 1f), ("Departamento", 1f), ("Aos cuidados de", 1f) }
            })
        };

        private readonly ChoiceBar _tabs;
        private readonly ProjectPanel _project;
        private readonly Control[] _pages;
        private readonly ThemedGrid _cables, _poles, _traction, _shortcuts;
        private readonly List<(string Command, string Title)> _shortcutCommands;
        private readonly Func<string, string?>? _conflict;
        private (string Text, bool Error)[] _shortcutState = new (string, bool)[0];
        private double[] _spans = new double[0];
        private readonly Dictionary<string, LabeledInput> _company = new Dictionary<string, LabeledInput>();
        private readonly LabeledInput _polePrefix, _ctoPrefix, _ceoPrefix;
        private readonly ChoiceBar _digits;
        private readonly Label _example;
        private readonly LabeledInput _textHeight, _boxSize, _arrowLength, _routeOffset, _roadLayer;
        private readonly ColorField _cableColor, _poleLabelColor, _boxLabelColor, _effortColor, _roadColor;
        private readonly LabeledCombo _roadLinetype, _roadWeight;
        private readonly StatusLabel _status;
        private readonly ThemedButton _ok, _defaults;

        /// <summary>Resultado (preenchidos enquanto tudo estiver certo).</summary>
        public List<CableModel> Cables { get; private set; } = new List<CableModel>();
        public List<PoleData> PoleTypes { get; private set; } = new List<PoleData>();
        public TractionTable Traction { get; private set; }
        public CompanyInfo Company { get; private set; } = new CompanyInfo();
        public UserSettings Settings { get; private set; }

        /// <summary>Aba Atalhos: comando → atalho ("" = sem atalho).</summary>
        public Dictionary<string, string> Shortcuts { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Aba Projeto: dados do projeto deste desenho e a escala 1:X digitada.</summary>
        public ProjectInfo Project => _project.Info;
        public int ScaleDenominator => _project.ScaleDenominator;

        /// <summary>O usuário clicou em Atualizar Blocos: depois de salvar, o comando roda o FIBRA_ATUALIZAR_BLOCOS.</summary>
        public bool UpdateBlocksAfter { get; private set; }

        /// <summary>Abre a pasta de dados no Explorer (botão Pasta de Dados da aba Projeto).</summary>
        public Func<string?>? OpenDataFolder { set => _project.OpenDataFolder = value; }

        /// <summary>Grava as alterações; retorna o erro (a janela continua aberta para tentar de novo) ou null.</summary>
        public Func<SettingsForm, string?>? SaveChanges { get; set; }

        /// <param name="cableNotes">Aviso da leitura dos cabos (linhas ignoradas, arquivo ilegível), mostrado na aba Cabos.</param>
        /// <param name="poleNotes">O mesmo para os postes, na aba Postes.</param>
        /// <param name="tractionNotes">O mesmo para a tabela de tração, na aba Tração.</param>
        /// <param name="drawingHasZone">O desenho já tem zona UTM: ela pode mudar, mas não ficar vazia.</param>
        /// <param name="initialTab">Aba aberta ao mostrar a janela (TabProject para o comando Dados do Projeto).</param>
        /// <param name="shortcuts">Atalhos atuais (comando → atalho); null = os padrões.</param>
        /// <param name="checkConflict">O que já existe no AutoCAD com esse nome (comando, LISP, acad.pgp); null se estiver livre.</param>
        public SettingsForm(List<CableModel> cables, List<PoleData> poles, TractionTable traction, CompanyInfo company,
            UserSettings settings, ProjectInfo project, int scale, bool drawingHasZone,
            string? cableNotes = null, string? poleNotes = null, string? tractionNotes = null, int initialTab = TabProject,
            IDictionary<string, string>? shortcuts = null, Func<string, string?>? checkConflict = null)
        {
            _conflict = checkConflict;
            Settings = settings.Clone();
            Traction = traction;
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Configurações";
            ClientSize = new Size(900, 720);

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_CONFIGURACOES",
                Title = "Configurações",
                Subtitle = "Dados do projeto, dados do plugin e o que ele coloca sozinho no desenho"
            };

            _tabs = new ChoiceBar(TabTitles);
            var tabBar = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(18, 10, 18, 4), BackColor = Theme.Background };
            tabBar.Controls.Add(_tabs);

            // ---------- Projeto (dados deste desenho) ----------
            _project = new ProjectPanel(project, scale, drawingHasZone);
            _project.Changed += (s, e) => { if (_ok != null) UpdateStatus(); };
            _project.UpdateBlocksClicked += (s, e) =>
            {
                UpdateBlocksAfter = true;
                Save();
                if (DialogResult != DialogResult.OK) UpdateBlocksAfter = false;
            };

            // ---------- Cabos ----------
            _cables = new ThemedGrid();
            _cables.AddText("Nome completo", 27);
            _cables.AddText("Nome curto", 16);
            _cables.AddText("Peso kg/km", 12, numeric: true);
            _cables.AddText("Fibras", 7, numeric: true);
            _cables.AddText("Diâm. mm", 11, numeric: true);
            _cables.AddText("Cor", 14).ReadOnly = true;
            _cables.AddCombo("Tipo de linha", 14, LinetypeLabels);
            _cables.AddCombo("Espessura", 11, WeightLabels);
            FillCables(cables);
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
            addCable.Click += (s, e) => _cables.AddRowAndEdit("", "", "", "", "", null, LinetypeLabels[0], LayerStyle.DefaultWeightLabel);
            removeCable.Click += (s, e) => _cables.RemoveCurrentRow();
            Control cablesPage = GridPage(_cables, new[] { addCable, removeCable },
                "Clique na cor para trocar (Delete volta para a cor padrão, na aba Desenho). Cor, tipo de linha e espessura vão para a layer " +
                "do cabo; as dos cabos já lançados mudam ao salvar. Espessura na tela: botão \"Mostrar espessura\" (LWDISPLAY). " +
                "Trocar o nome curto de um cabo já lançado faz o desenho perder o vínculo com o cadastro.",
                cableNotes);

            // ---------- Postes ----------
            _poles = new ThemedGrid();
            _poles.AddCombo("Tipo", 30, new[] { PoleData.DoubleT, PoleData.Circular });
            _poles.AddText("Altura (m)", 35, numeric: true);
            _poles.AddText("Esforço nominal (daN)", 35, numeric: true);
            FillPoles(poles);

            var addPole = new ThemedButton("Adicionar modelo", false);
            var removePole = new ThemedButton("Remover modelo", false);
            addPole.Click += (s, e) => _poles.AddRowAndEdit(PoleData.DoubleT, "", "");
            removePole.Click += (s, e) => _poles.RemoveCurrentRow();
            Control polesPage = GridPage(_poles, new[] { addPole, removePole },
                "Modelos oferecidos no Inserir Postes. DT = Duplo T, CC = Circular. Altura em metros e esforço em daN, como na norma " +
                "(o desenho mostra 11/300).", poleNotes);

            // ---------- Tração ----------
            _traction = new ThemedGrid();
            _traction.DefaultCellStyle.Padding = new Padding(1, 0, 3, 0);
            _traction.ColumnHeadersDefaultCellStyle.Padding = new Padding(1, 0, 1, 0);
            FillTraction(traction);
            var addRange = new ThemedButton("Adicionar faixa", false);
            var removeRange = new ThemedButton("Remover faixa", false);
            addRange.Click += (s, e) => _traction.AddRowAndEdit(new object?[] { "" }.Concat(_spans.Select(_ => (object?)"")).ToArray());
            removeRange.Click += (s, e) => _traction.RemoveCurrentRow();
            Control tractionPage = GridPage(_traction, new[] { addRange, removeRange },
                "Tabela 08 da NDU 009: tração em kgf por faixa de fibras (linhas) e vão em metros (colunas), cabo autossustentado, flecha " +
                "de 1%. Fibras entre as faixas usam a faixa seguinte. Usada quando os Parâmetros de Cálculo estão em \"Tabela 08\".",
                tractionNotes);

            // ---------- Empresa ----------
            var companyRows = new List<(Control, float)>();
            foreach (var (section, rows) in CompanyLayout)
            {
                companyRows.Add((new SectionLabel(section), companyRows.Count == 0 ? 22 : 28));
                foreach (var row in rows)
                {
                    var inputs = row.Select(f => (Control: (Control)(_company[f.Field] = new LabeledInput(f.Field, f.Field == "Tipo de companhia" ? "Internet" : "")), f.Width)).ToArray();
                    companyRows.Add((WeightedRow(inputs), 52));
                }
            }
            companyRows.Add((Hint("Vão para o Memorial Descritivo e a Tabela A do relatório. Campo vazio não aparece no documento. RG, CPF e " +
                                  "endereço do representante ficam só neste computador (Documentos\\Fiber Plugin\\empresa.txt)."), 40));
            Control companyPage = FormPage(companyRows.ToArray());
            foreach (var (_, rows) in CompanyLayout)
                foreach (var row in rows)
                    foreach (var (field, _) in row)
                        _company[field].Value = company[field];

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

            // ---------- Desenho: tamanhos, cores e ruas ----------
            _textHeight = new LabeledInput("Altura dos textos (mm)", "ex.: 2.0");
            _boxSize = new LabeledInput("Símbolo da CTO/CEO (mm)", "ex.: 7.0");
            _arrowLength = new LabeledInput("Seta de esforço (mm)", "ex.: 18");
            _routeOffset = new LabeledInput("Afastamento do cabo (m)", "ex.: 1.8");
            _cableColor = new ColorField("Cabos sem cor própria");
            _poleLabelColor = new ColorField("Textos dos postes");
            _boxLabelColor = new ColorField("Textos das CTO/CEO");
            _effortColor = new ColorField("Setas de esforço");
            _roadLayer = new LabeledInput("Layer das ruas", "ex.: RUAS");
            _roadColor = new ColorField("Cor das ruas");
            _roadLinetype = new LabeledCombo("Tipo de linha das ruas", LinetypeLabels);
            _roadWeight = new LabeledCombo("Espessura das ruas", WeightLabels);
            Control drawingPage = FormPage(new (Control, float)[]
            {
                (new SectionLabel("Tamanhos no papel (escala 1:1000) e roteamento automático"), 26),
                (Row(_textHeight, _boxSize, _arrowLength, _routeOffset), 56),
                (Hint("Em outra escala (aba Projeto) os tamanhos acompanham: em 1:2000 ficam o dobro no desenho. Ao salvar, o plugin " +
                      "pergunta se ajusta os textos já desenhados; símbolos, setas e o afastamento valem para os próximos."), 40),
                (new SectionLabel("Cores das layers"), 30),
                (Row(_cableColor, _poleLabelColor, _boxLabelColor, _effortColor), 56),
                (new SectionLabel("Ruas (Importar Ruas)"), 30),
                (Row(_roadLayer, _roadColor, _roadLinetype, _roadWeight), 56),
                (Hint("O Importar Ruas coloca tudo nessa layer: contorno das ruas e calçadas, eixos e nomes. Ao salvar, as layers deste " +
                      "desenho mudam de cor e aparência (e a das ruas muda de nome, se você trocar o nome)."), 40)
            });

            // ---------- Atalhos ----------
            _shortcuts = new ThemedGrid();
            _shortcuts.AddText("Comando", 30).ReadOnly = true;
            DataGridViewTextBoxColumn commandColumn = _shortcuts.AddText("Nome no AutoCAD", 26);
            commandColumn.ReadOnly = true;
            commandColumn.DefaultCellStyle.ForeColor = Theme.Muted;
            _shortcuts.AddText("Atalho", 11);
            _shortcuts.AddText("Situação", 40).ReadOnly = true;
            _shortcutCommands = ToolCatalog.AllCommands.Where(c => ShortcutSettings.Defaults.ContainsKey(c.Command)).ToList();
            if (shortcuts != null) FillShortcuts(shortcuts);
            else FillShortcuts(ShortcutSettings.Defaults);
            // Atalho sempre em maiúsculas, como o AutoCAD mostra
            _shortcuts.CellParsing += (s, e) =>
            {
                if (e.ColumnIndex != ShortcutAlias || e.Value is not string text) return;
                e.Value = ShortcutSettings.Normalize(text);
                e.ParsingApplied = true;
            };
            _shortcuts.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex != ShortcutState || e.RowIndex < 0 || e.RowIndex >= _shortcutState.Length || e.CellStyle == null) return;
                var (text, error) = _shortcutState[e.RowIndex];
                e.Value = text;
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = error ? Theme.Error : Theme.Muted;
                e.FormattingApplied = true;
            };
            _shortcuts.KeyDown += (s, e) =>
            {
                if (e.KeyCode is not (Keys.Delete or Keys.Back) || _shortcuts.CurrentCell?.ColumnIndex != ShortcutAlias || _shortcuts.IsCurrentCellInEditMode) return;
                _shortcuts.CurrentCell.Value = "";
                e.Handled = true;
            };
            var clearShortcut = new ThemedButton("Tirar atalho", false);
            clearShortcut.Click += (s, e) =>
            {
                if (_shortcuts.CurrentCell == null) return;
                _shortcuts.EndEdit();
                _shortcuts.Rows[_shortcuts.CurrentCell.RowIndex].Cells[ShortcutAlias].Value = "";
            };
            Control shortcutsPage = GridPage(_shortcuts, new[] { clearShortcut },
                "Digite o atalho na linha de comando e tecle Enter ou Espaço, como os atalhos do AutoCAD (L = LINHA, CO = COPIAR). " +
                "Só letras e números. Delete ou \"Tirar atalho\" deixa o comando sem atalho. A Situação avisa quando o atalho já é um " +
                "comando ou um atalho do AutoCAD (acad.pgp) ou se repete; com conflito, o Salvar não libera.", null);

            _pages = new Control[] { _project, cablesPage, polesPage, tractionPage, companyPage, namesPage, drawingPage, shortcutsPage };
            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(18, 4, 18, 0) };
            foreach (Control page in _pages)
            {
                page.Dock = DockStyle.Fill;
                pages.Controls.Add(page);
            }

            _status = new StatusLabel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(18, 0, 18, 0) };

            var footer = new FooterPanel { Hint = "Projeto: no desenho · o resto: Documentos\\Fiber Plugin" };
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

            FillNames(settings);
            FillDrawing(settings);

            var inputs2 = new List<LabeledInput> { _polePrefix, _ctoPrefix, _ceoPrefix, _textHeight, _boxSize, _arrowLength, _routeOffset, _roadLayer };
            inputs2.AddRange(_company.Values);
            foreach (LabeledInput input in inputs2) input.Box.Input.TextChanged += (s, e) => UpdateStatus();
            foreach (ColorField field in new[] { _cableColor, _poleLabelColor, _boxLabelColor, _effortColor, _roadColor })
            {
                field.ValueChanged += (s, e) => { UpdateStatus(); _cables.Invalidate(); };
            }
            _roadLinetype.ValueChanged += (s, e) => UpdateStatus();
            _roadWeight.ValueChanged += (s, e) => UpdateStatus();
            _digits.SelectedChanged += (s, e) => UpdateStatus();
            foreach (ThemedGrid grid in new[] { _cables, _poles, _traction, _shortcuts })
            {
                grid.CellValueChanged += (s, e) => UpdateStatus();
                grid.RowsRemoved += (s, e) => UpdateStatus();
                grid.RowsAdded += (s, e) => UpdateStatus();
            }
            // A aba inicial é marcada antes de ligar o evento: as páginas só se escondem no Load (por causa do DPI)
            _tabs.SelectedIndex = Math.Max(0, Math.Min(TabTitles.Length - 1, initialTab));
            _tabs.SelectedChanged += (s, e) => { ShowPage(_tabs.SelectedIndex); UpdateStatus(); };
            Shown += (s, e) => { if (_tabs.SelectedIndex == TabProject) _project.FocusFirst(); };
            _defaults.Click += (s, e) => RestoreDefaults();
            _ok.Click += (s, e) => Save();

            // As páginas ficam visíveis até a janela carregar: escondidas antes disso não acompanham a escala do DPI
            Load += (s, e) =>
            {
                ShowPage(_tabs.SelectedIndex);
                foreach (ThemedGrid grid in new[] { _cables, _poles, _traction, _shortcuts }) grid.ClearSelection();
            };
            UpdateStatus();
        }

        private void ShowPage(int index)
        {
            if (index < 0) return;
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == index;
            _pages[index].BringToFront();
            _defaults.Visible = index != TabCompany && index != TabProject; // Empresa e projeto não têm padrão
        }

        // ---------- Preenchimento ----------

        private void FillCables(IEnumerable<CableModel> cables) =>
            _cables.ReplaceRows(cables.Select(c => new object?[]
            {
                c.FullName, c.ShortName, Num(c.WeightKgKm), c.Fibers?.ToString() ?? "", c.DiameterMm is double d ? Num(d) : "", c.Color,
                LayerStyle.LinetypeLabel(c.Linetype), LayerStyle.WeightLabel(c.LineWeightMm)
            }));

        private void FillPoles(IEnumerable<PoleData> poles) =>
            _poles.ReplaceRows(poles.Select(p => new object?[] { p.Type, Num(p.HeightM), Num(p.EffortDaN) }));

        /// <summary>Uma coluna por vão da tabela (os vãos vêm da própria tabela).</summary>
        private void FillTraction(TractionTable table)
        {
            _spans = table.Spans;
            _traction.Rows.Clear();
            _traction.Columns.Clear();
            _traction.AddText("Fibras", 2.6f);
            foreach (double span in _spans) _traction.AddText(Num(span), 1f, numeric: true);
            _traction.ReplaceRows(table.Rows.Select(r => new object?[] { r.Label }.Concat(r.Values.Select(v => (object?)Num(v))).ToArray()));
        }

        /// <summary>Uma linha por comando, na ordem da aba (menu, botões, linha de comando).</summary>
        private void FillShortcuts(IReadOnlyDictionary<string, string> shortcuts) =>
            _shortcuts.ReplaceRows(_shortcutCommands.Select(c => new object?[]
            {
                c.Title, c.Command, shortcuts.TryGetValue(c.Command, out string? alias) ? ShortcutSettings.Normalize(alias) : "", ""
            }));

        private void FillShortcuts(IDictionary<string, string> shortcuts) =>
            FillShortcuts(new Dictionary<string, string>(shortcuts, StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, string>);

        private void FillNames(UserSettings s)
        {
            _polePrefix.Value = s.PolePrefix;
            _ctoPrefix.Value = s.CtoPrefix;
            _ceoPrefix.Value = s.CeoPrefix;
            _digits.SelectedIndex = s.NumberDigits - 1;
        }

        private void FillDrawing(UserSettings s)
        {
            _textHeight.Value = Num(s.TextHeight);
            _boxSize.Value = Num(s.BoxSymbolSize);
            _arrowLength.Value = Num(s.EffortArrowLength);
            _routeOffset.Value = Num(s.AutoRouteOffset);
            _cableColor.Value = s.CableColor;
            _poleLabelColor.Value = s.PoleLabelColor;
            _boxLabelColor.Value = s.BoxLabelColor;
            _effortColor.Value = s.EffortColor;
            _roadLayer.Value = s.RoadLayer;
            _roadColor.Value = s.RoadColor;
            _roadLinetype.Value = LayerStyle.LinetypeLabel(s.RoadLinetype);
            _roadWeight.Value = LayerStyle.WeightLabel(s.RoadLineWeightMm);
        }

        /// <summary>"Restaurar padrão" da aba aberta: os valores que vêm com o plugin.</summary>
        private void RestoreDefaults()
        {
            switch (_tabs.SelectedIndex)
            {
                case TabCables: FillCables(CableProvider.Defaults()); break;
                case TabPoles: FillPoles(PoleModels.Defaults()); break;
                case TabTraction: FillTraction(TractionTable.Default()); break;
                case TabNames: FillNames(new UserSettings()); break;
                case TabShortcuts: FillShortcuts(ShortcutSettings.Defaults); break;
                default: FillDrawing(new UserSettings()); break;
            }
            UpdateStatus();
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

        // ---------- Conferência e resultado ----------

        private void Save()
        {
            // Célula ainda em edição entra no que vai ser gravado
            foreach (ThemedGrid grid in new[] { _cables, _poles, _traction, _shortcuts }) grid.EndEdit();
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

        /// <summary>Confere as seis abas e monta o resultado. O primeiro problema aparece na linha de status.</summary>
        private void UpdateStatus()
        {
            string? cableError = ReadCables(out List<CableModel> cables);
            string? poleError = ReadPoles(out List<PoleData> poles);
            string? tractionError = ReadTraction(out TractionTable? traction);
            CompanyInfo company = ReadCompany();
            string? settingsError = ReadSettings(out UserSettings settings);
            string? shortcutError = ReadShortcuts(out Dictionary<string, string> shortcuts);
            string? error = _project.Error ?? cableError ?? poleError ?? tractionError ?? settingsError ?? shortcutError;
            if (error == null)
            {
                Shortcuts = shortcuts;
                Cables = cables;
                PoleTypes = poles;
                Traction = traction!;
                Company = company;
                Settings = settings;
                _example.Text = $"{Settings.Name(Settings.PolePrefix, 7)}    {Settings.Name(Settings.CtoPrefix, 3)}    {Settings.Name(Settings.CeoPrefix, 1)}";
                if (_tabs.SelectedIndex == TabProject)
                    _status.Set($"Projeto deste desenho: {_project.Summary}" + (Project.Route.Length > 0 ? $" · {Project.Route}" : ""), StatusKind.Ok);
                else if (_tabs.SelectedIndex == TabShortcuts)
                    _status.Set($"{Shortcuts.Count(s => s.Value.Length > 0)} de {Shortcuts.Count} comandos com atalho, sem conflitos · " +
                                "digite o atalho na linha de comando e tecle Enter", StatusKind.Ok);
                else
                    _status.Set($"{Cables.Count} cabos · {PoleTypes.Count} modelos de poste · tração com {Traction.Rows.Count} faixas · " +
                            $"empresa: {(Company.IsEmpty ? "não preenchida" : Company.LegalName)} · " +
                            $"{Settings.Name(Settings.PolePrefix, 1)}, {Settings.Name(Settings.CtoPrefix, 1)}, {Settings.Name(Settings.CeoPrefix, 1)} · " +
                            $"ruas na layer {Settings.RoadLayer}", StatusKind.Ok);
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
                    Color = _cables.Rows[i].Cells[CableColor].Value as short?,
                    Linetype = LayerStyle.LinetypeFromLabel(_cables.CellText(i, CableLinetype)),
                    LineWeightMm = LayerStyle.WeightFromText(_cables.CellText(i, CableLineWeight))
                });
            }
            return cables.Count == 0 ? "Cabos: cadastre pelo menos um cabo." : null;
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
            return poles.Count == 0 ? "Postes: cadastre pelo menos um modelo." : null;
        }

        private string? ReadTraction(out TractionTable? table)
        {
            table = null;
            var rows = new List<TractionRow>();
            for (int i = 0; i < _traction.Rows.Count; i++)
            {
                string label = _traction.CellText(i, 0);
                var texts = Enumerable.Range(1, _spans.Length).Select(c => _traction.CellText(i, c)).ToList();
                if (label.Length == 0 && texts.All(t => t.Length == 0)) continue;

                string where = $"Tração, linha {i + 1}";
                var row = new TractionRow(label, new double[_spans.Length]);
                if (row.MaxFibers == null) return $"{where}: a faixa de fibras termina num número (ex.: 2-12 ou 96).";
                if (rows.Any(r => r.MaxFibers == row.MaxFibers)) return $"{where}: a faixa até {row.MaxFibers} fibras já existe.";
                for (int c = 0; c < _spans.Length; c++)
                {
                    if (!DataFiles.TryParseNumber(texts[c], out double value) || value <= 0)
                        return $"{where}: informe a tração em kgf no vão de {Num(_spans[c])} m.";
                    row.Values[c] = value;
                }
                rows.Add(row);
            }
            if (rows.Count == 0) return "Tração: a tabela precisa de pelo menos uma faixa de fibras.";
            table = new TractionTable(_spans, rows);
            return null;
        }

        /// <summary>
        /// Atalhos digitados e a situação de cada um (coluna Situação): forma errada, repetido com outro comando do plugin
        /// ou já existente no AutoCAD (comando, LISP ou acad.pgp). Retorna o primeiro problema.
        /// </summary>
        private string? ReadShortcuts(out Dictionary<string, string> map)
        {
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var aliases = new string[_shortcutCommands.Count];
            for (int i = 0; i < aliases.Length && i < _shortcuts.Rows.Count; i++)
            {
                aliases[i] = ShortcutSettings.Normalize(_shortcuts.CellText(i, ShortcutAlias));
                map[_shortcutCommands[i].Command] = aliases[i];
            }

            string? first = null;
            _shortcutState = new (string, bool)[aliases.Length];
            for (int i = 0; i < aliases.Length; i++)
            {
                string alias = aliases[i] ?? "";
                if (alias.Length == 0)
                {
                    _shortcutState[i] = ("sem atalho", false);
                    continue;
                }
                int other = Array.FindIndex(aliases, a => a == alias);
                if (other == i) other = Array.FindIndex(aliases, i + 1, a => a == alias);
                string? problem = ShortcutSettings.SyntaxError(alias)
                                  ?? (other >= 0 ? $"repetido com {_shortcutCommands[other].Title}" : null)
                                  ?? _conflict?.Invoke(alias);
                _shortcutState[i] = problem != null ? ("⚠ " + problem, true) : ("ok", false);
                if (problem != null && first == null) first = $"Atalhos: {alias} ({_shortcutCommands[i].Title}) {problem}.";
            }
            _shortcuts.InvalidateColumn(ShortcutState);
            return first;
        }

        private CompanyInfo ReadCompany()
        {
            var company = new CompanyInfo();
            foreach (var pair in _company) company[pair.Key] = pair.Value.Value;
            return company;
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
                if (!DataFiles.TryParseNumber(input.Value, out double value)) return $"Desenho: informe {input.Title.ToLowerInvariant()} (ex.: {input.Box.Placeholder.Replace("ex.: ", "")}).";
                apply(value);
            }

            s.CableColor = _cableColor.Value;
            s.PoleLabelColor = _poleLabelColor.Value;
            s.BoxLabelColor = _boxLabelColor.Value;
            s.EffortColor = _effortColor.Value;
            s.RoadLayer = _roadLayer.Value;
            s.RoadColor = _roadColor.Value;
            s.RoadLinetype = LayerStyle.LinetypeFromLabel(_roadLinetype.Value);
            s.RoadLineWeightMm = LayerStyle.WeightFromText(_roadWeight.Value);

            string? error = s.Validate();
            if (error == null) return null;
            bool names = error.StartsWith("Prefixo") || error.StartsWith("Os prefixos") || error.Contains("dígitos");
            return (names ? "Nomes: " : "Desenho: ") + error;
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
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
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
        private static Control Row(params Control[] controls) => WeightedRow(controls.Select(c => (c, 1f)).ToArray());

        /// <summary>Campos lado a lado, cada um com a largura relativa dada.</summary>
        private static Control WeightedRow((Control Control, float Width)[] controls)
        {
            var row = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            float total = controls.Sum(c => c.Width);
            for (int i = 0; i < controls.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f * controls[i].Width / total));
                controls[i].Control.Dock = DockStyle.Fill;
                controls[i].Control.Margin = new Padding(i == 0 ? 0 : 6, 0, i == controls.Length - 1 ? 0 : 6, 0);
                row.Controls.Add(controls[i].Control, i, 0);
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
