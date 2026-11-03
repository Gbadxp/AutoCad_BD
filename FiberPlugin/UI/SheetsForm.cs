using System.Drawing.Drawing2D;
using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Janela do Gerar Folhas. À esquerda as escolhas: área do projeto (marcada no desenho, a da rede ou o desenho todo),
    /// tamanho e orientação da folha, escala, sobreposição e prefixo dos nomes; cada uma tem um "?" que mostra o que ela faz
    /// (em destaque quando há um aviso). À direita a prévia da divisão (as folhas que serão criadas, com o nome, e os
    /// pedaços vazios que ficam de fora) e o resumo. Confere enquanto digita. Embaixo, gerar as folhas (apagando antes as
    /// do mesmo prefixo) ou só apagar as que já existem com o prefixo.
    /// </summary>
    internal class SheetsForm : Form
    {
        private const int MaxSheets = 300;   // Mais que isso é escala ou área errada
        private const int ManySheets = 60;   // Acima disso ainda gera, com aviso
        private const long MaxGridCells = 3000; // Grade (com os pedaços vazios) grande demais nem é planejada

        private const string SheetContents =
            "O que cada folha terá (NBR 16752):\n" +
            "•  Quadro com margem de 20 mm à esquerda, malha de referência (letras e números na margem), marcas de centro e de corte.\n" +
            "•  Legenda (carimbo) de 180 mm deitada no canto de baixo à direita: logo e empresa, projeto, endereço, município, CEP, " +
            "responsável técnico com o CREA, a ART e a assinatura, conteúdo, área rural ou urbana, escala, data e folha (01/06). Os dados vêm " +
            "da Configuração de Projeto (abas Projeto, Carimbo e Empresa); a logo, de Dados\\Memorial\\logo.png, a mesma do memorial.\n" +
            "•  À esquerda da legenda (no A4 em pé, em cima dela), a articulação das folhas: o mapa da divisão com o número de cada folha " +
            "e a folha atual hachurada, e o formato no canto (FOLHA A2). Sobrando espaço, o nome do desenho.\n" +
            "•  Coluna à direita com a seta do norte e a legenda dos símbolos usados no desenho (NDU 009, item 16.3), todos: as linhas " +
            "diminuem se precisar.\n" +
            "•  Na margem esquerda, os avisos da Lei 5.194/66 e da propriedade dos desenhos.\n" +
            "•  Marcas de dobramento nas bordas (a cópia dobrada fica do tamanho A4, com a legenda à vista).\n" +
            "•  Viewport travado na escala, mostrando o pedaço da prévia com o mesmo nome.\n" +
            "•  Já configurada para DWG To PDF: para gerar o PDF, use o comando PUBLICAR e escolha as folhas.\n" +
            "•  No Model, os retângulos e nomes das folhas ficam na layer FIBRA_FOLHAS, que não imprime.";

        private readonly IReadOnlyList<Rect> _content;
        private readonly Rect? _network, _whole;
        private readonly int _drawingScale;
        private readonly Func<string, int> _existingSheets;
        private readonly ThemedButton _pick, _useNetwork, _useWhole;
        private readonly Label _areaText;
        private readonly HelpButton _areaHelp, _sheetHelp, _scaleHelp, _overlapHelp, _prefixHelp, _previewHelp;
        private readonly ChoiceBar _format, _orientation;
        private readonly LabeledInput _scale, _overlap, _prefix;
        private readonly SheetPreview _preview;
        private readonly StatusLabel _status;
        private readonly ThemedButton _ok, _delete;
        private Rect? _area;
        private string _areaSource;
        private string _prefixText = "";
        private int _existing; // Folhas que já existem com o prefixo digitado

        /// <summary>Marca os dois cantos da área no desenho (com a janela escondida); null se cancelar.</summary>
        public Func<Rect?>? PickInDrawing { get; set; }

        /// <summary>Botão Apagar folhas: o prefixo das folhas a apagar (null = gerar, ou cancelou).</summary>
        public string? DeletePrefix { get; private set; }

        /// <summary>O que foi escolhido (null enquanto faltar algo ou houver erro).</summary>
        public SheetSettings? Result { get; private set; }

        /// <summary>A divisão em folhas do que foi escolhido.</summary>
        public SheetPlan? Plan { get; private set; }

        /// <param name="saved">Escolhas da última vez neste desenho.</param>
        /// <param name="drawingScale">Escala do desenho (Configuração de Projeto).</param>
        /// <param name="content">Extensões dos elementos do Model: a prévia e os pedaços vazios saem delas.</param>
        /// <param name="network">Área da rede (postes, cabos e caixas do plugin); null se o desenho não tiver.</param>
        /// <param name="existingSheets">Quantas folhas com o prefixo dado já existem no desenho.</param>
        public SheetsForm(SheetSettings saved, int drawingScale, IReadOnlyList<Rect> content, Rect? network, Func<string, int> existingSheets)
        {
            _content = content;
            _network = network;
            _whole = Extents(content);
            _drawingScale = drawingScale;
            _existingSheets = existingSheets;

            Theme.ApplyForm(this);
            // Os tamanhos daqui são multiplicados pela escala da tela (S): no AutoCAD a escala automática do Windows Forms não
            // acompanhava a fonte (em 125% os textos cresciam e as linhas, não)
            AutoScaleMode = AutoScaleMode.None;
            Text = "Fiber Plugin - Gerar Folhas";
            ClientSize = new Size(S(980), S(440));

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_GERAR_FOLHAS",
                Title = "Gerar Folhas",
                Subtitle = "Divide o projeto em folhas A0 a A4 prontas para imprimir ou gerar o PDF, cada uma num layout"
            };

            // ---------- Área do projeto ----------
            _pick = new ThemedButton("Marcar no desenho", false) { Margin = new Padding(0, S(4), S(6), S(4)) };
            _useNetwork = new ThemedButton("Área da rede", false) { Margin = new Padding(0, S(4), S(6), S(4)), Enabled = network != null };
            _useWhole = new ThemedButton("Desenho todo", false) { Margin = new Padding(0, S(4), 0, S(4)), Enabled = _whole != null };
            var areaButtons = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
            areaButtons.Controls.AddRange(new Control[] { _pick, _useNetwork, _useWhole });
            _areaText = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty };
            _areaHelp = new HelpButton("Área do projeto")
            {
                HelpText = "A parte do desenho que vira folhas. Ela é dividida numa grade de folhas do tamanho e da escala escolhidos; " +
                           "pedaço da grade sem nada desenhado não vira folha.\n\n" +
                           "•  Marcar no desenho: a janela some e você clica os dois cantos.\n" +
                           "•  Área da rede: onde há postes, cabos e caixas do plugin, com uma folga.\n" +
                           "•  Desenho todo: tudo o que tem no Model.\n\n" +
                           "Na próxima vez, a janela volta com a área usada."
            };

            // ---------- Folha ----------
            _format = new ChoiceBar(SheetFormat.All.Select(f => f.Name).ToArray());
            foreach (Control button in _format.Controls) button.Width = S(56); // "A0" a "A4": botões estreitos, os cinco numa linha
            _orientation = new ChoiceBar("Automática", "Paisagem", "Retrato");
            TableLayoutPanel sheetGrid = FormLayout.OptionGrid(S(96), S(36), ("Tamanho", _format), ("Orientação", _orientation));
            _sheetHelp = new HelpButton("Folha");

            // ---------- Escala, sobreposição e nomes ----------
            _scale = new LabeledInput("Escala (1:X)", "ex.: 1000");
            _overlap = new LabeledInput("Sobreposição %", "ex.: 5");
            _prefix = new LabeledInput("Prefixo", "ex.: FL");
            _scaleHelp = new HelpButton("Escala");
            _overlapHelp = new HelpButton("Sobreposição");
            _prefixHelp = new HelpButton("Prefixo dos nomes");

            var left = Column(
                (Section("Área do projeto", _areaHelp), 30), (areaButtons, 36), (_areaText, 24),
                (Section("Folha", _sheetHelp), 32), (sheetGrid, 72),
                (Section("Escala, sobreposição e nomes", null), 32),
                (Fields((_scale, _scaleHelp), (_overlap, _overlapHelp), (_prefix, _prefixHelp)), 56));

            // ---------- Prévia ----------
            _preview = new SheetPreview { Margin = new Padding(0, S(2), 0, S(2)) };
            _status = new StatusLabel { Margin = Padding.Empty };
            _previewHelp = new HelpButton("Prévia das folhas")
            {
                HelpText = "Cada retângulo azul é uma folha (um layout), com o nome dela. Os tracejados são pedaços da grade sem nada " +
                           "desenhado: não viram folha. A linha tracejada clara é a área escolhida.\n\n" + SheetContents
            };
            var right = Column((Section("Prévia das folhas", _previewHelp), 30), (_preview, 0), (_status, 36));
            right.Padding = new Padding(S(16), 0, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(S(18), S(6), S(18), S(8)), BackColor = Theme.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(left, 0, 0);
            body.Controls.Add(right, 1, 0);

            var footer = new FooterPanel { Hint = "Clique no ? para ver o que cada opção faz  ·  as escolhas ficam gravadas neste desenho" };
            _ok = new ThemedButton("Apagar e gerar novas", true); // Largura do texto mais longo; o texto muda com o prefixo
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            _delete = new ThemedButton("Apagar folhas", false);
            footer.AddButton(_ok);
            footer.AddButton(cancel);
            footer.AddButton(_delete);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            // ---------- Valores iniciais ----------
            _format.SelectedIndex = Math.Max(0, Array.IndexOf(SheetFormat.All, saved.SheetFormat));
            _orientation.SelectedIndex = saved.Orientation;
            _scale.Value = (saved.Scale > 0 ? saved.Scale : drawingScale).ToString(CultureInfo.InvariantCulture);
            _overlap.Value = saved.OverlapPercent.ToString("0.##", CultureInfo.InvariantCulture);
            _prefix.Value = saved.Prefix;
            _area = saved.Area ?? network;
            _areaSource = saved.Area != null ? "a mesma da última vez" : network != null ? "a da rede" : "";

            // ---------- Eventos ----------
            _format.SelectedChanged += (s, e) => UpdateAll();
            _orientation.SelectedChanged += (s, e) => UpdateAll();
            foreach (LabeledInput field in new[] { _scale, _overlap, _prefix }) field.Box.Input.TextChanged += (s, e) => UpdateAll();
            _pick.Click += (s, e) =>
            {
                if (PickInDrawing?.Invoke() is not Rect picked) return;
                UseArea(picked, "marcada no desenho");
            };
            _useNetwork.Click += (s, e) => { if (_network is Rect r) UseArea(r, "a da rede (postes, cabos e caixas)"); };
            _useWhole.Click += (s, e) => { if (_whole is Rect r) UseArea(r, "o desenho todo"); };
            _ok.Click += (s, e) =>
            {
                if (Result == null) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            _delete.Click += (s, e) =>
            {
                if (_existing == 0) return;
                string sheets = _existing == 1 ? $"a folha {_prefixText}-..." : $"as {_existing} folhas {_prefixText}-...";
                if (MessageBox.Show(this, $"Apagar {sheets} deste desenho, junto com o quadro delas no Model?", "Fiber Plugin - Apagar folhas",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                DeletePrefix = _prefixText;
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (s, e) =>
            {
                _pick.Visible = PickInDrawing != null;
                (_area == null && PickInDrawing != null ? (Control)_pick : _scale.Box.Input).Focus();
            };

            UpdateAll();
        }

        /// <summary>Pixels de 96 dpi na escala da tela.</summary>
        private int S(int pixels) => Theme.Scale(this, pixels);

        private void UseArea(Rect area, string source)
        {
            _area = area;
            _areaSource = source;
            UpdateAll();
        }

        /// <summary>Confere tudo, refaz a divisão e atualiza as explicações dos "?", a prévia e o resumo.</summary>
        private void UpdateAll()
        {
            Result = null;
            Plan = null;

            _areaText.Text = _area is Rect a ? $"Área: {a.Width:N0} × {a.Height:N0} m  ·  {_areaSource}" : "Nenhuma área escolhida ainda";
            _areaText.ForeColor = _area != null ? Theme.Text : Theme.Muted;

            SheetFormat format = SheetFormat.All[Math.Max(0, _format.SelectedIndex)];
            int orientation = Math.Max(0, _orientation.SelectedIndex);
            int? scale = ParseScale(_scale.Value);
            double? overlap = DataFiles.TryParseNumber(_overlap.Value, out double o) && o >= 0 && o <= SheetSettings.MaxOverlapPercent ? o : (double?)null;
            string prefix = _prefix.Value.Trim();
            int existing = prefix.Length > 0 ? _existingSheets(prefix) : 0;
            _prefixText = prefix;
            _existing = existing;
            _delete.Enabled = existing > 0; // Apagar não depende do resto estar certo

            string? error = null;
            bool incomplete = false;
            if (_area is not Rect area)
            {
                error = "Escolha a área do projeto: marque no desenho ou use a área da rede.";
                incomplete = true;
            }
            else if (area.Width < 1 || area.Height < 1) error = "Área muito pequena: marque os dois cantos de novo.";
            else if (scale == null) error = $"Escala inválida: use um número de {DrawingScale.Min} a {DrawingScale.Max} (ex.: 1000 para 1:1000).";
            else if (overlap == null) error = $"Sobreposição inválida: use de 0 a {SheetSettings.MaxOverlapPercent:0} (%).";
            else if (prefix.Length == 0) error = "Escreva o prefixo dos nomes das folhas (ex.: FL).";
            else if (prefix.IndexOfAny(LayerStyle.InvalidNameChars) >= 0) error = "O prefixo não pode ter < > / \\ \" : ; ? * | , = `";

            var settings = new SheetSettings
            {
                Format = format.Name,
                Orientation = orientation,
                Scale = scale ?? 0,
                OverlapPercent = overlap ?? 0,
                Prefix = prefix,
                Area = _area
            };
            SheetPlan? plan = null;
            if (error == null)
            {
                long cells = GridCells(settings);
                if (cells > MaxGridCells) error = TooMany($"cerca de {cells:N0}", settings);
                else plan = settings.Plan(_content);
            }
            if (plan != null && plan.Tiles.Count == 0)
                error = "Nenhum elemento do desenho dentro da área: nenhuma folha seria criada.";
            else if (plan != null && plan.Tiles.Count > MaxSheets)
                error = TooMany(plan.Tiles.Count.ToString(CultureInfo.InvariantCulture), settings);

            Explain(format, orientation, scale, overlap, prefix, existing, plan);

            bool ok = error == null && plan != null;
            _preview.SetPlan(_area, _content, ok || plan?.Tiles.Count == 0 ? plan : null, prefix);
            _ok.Enabled = ok;
            _ok.Text = existing > 0 ? "Apagar e gerar novas" : "Gerar folhas";
            if (!ok)
            {
                _status.Set(error ?? "", incomplete ? StatusKind.Hint : StatusKind.Error);
                return;
            }

            Result = settings;
            Plan = plan;
            int count = plan!.Tiles.Count;
            string summary = $"{count} folha{(count == 1 ? "" : "s")} {format.Name} {(plan.Landscape ? "paisagem" : "retrato")} em 1:{plan.Scale}" +
                             $"  ·  grade {plan.Columns} × {plan.Rows}" +
                             (plan.SkippedEmpty > 0 ? $"  ·  {plan.SkippedEmpty} pedaço{(plan.SkippedEmpty == 1 ? " vazio fica" : "s vazios ficam")} de fora" : "") +
                             (existing > 0 ? $"  ·  substitui {existing} folha{(existing == 1 ? "" : "s")} {prefix}-..." : "");
            if (count > ManySheets) _status.Set(summary + "  ·  são muitas folhas: confira a escala", StatusKind.Hint);
            else _status.Set(summary, StatusKind.Ok);
        }

        /// <summary>As explicações dos "?", com os números do que foi escolhido; o "?" fica em destaque quando há aviso.</summary>
        private void Explain(SheetFormat format, int orientation, int? scale, double? overlap, string prefix, int existing, SheetPlan? plan)
        {
            bool landscape = orientation == SheetSettings.Landscape || (orientation == SheetSettings.Automatic && (plan?.Landscape ?? true));
            Rect viewport = format.ViewportArea(landscape);
            string paper = $"{format.Name} = {format.LongSide:0} × {format.ShortSide:0} mm.";
            string covers = scale is int s
                ? $" Em {(landscape ? "paisagem" : "retrato")}, cada folha mostra {viewport.Width * s / 1000:N0} × {viewport.Height * s / 1000:N0} m do " +
                  $"desenho em 1:{s}; o resto da folha é a moldura, a legenda embaixo e a coluna do norte e dos símbolos."
                : "";
            string automatic = plan != null && orientation == SheetSettings.Automatic ? $" (agora ficou {(plan.Landscape ? "paisagem" : "retrato")})" : "";
            _sheetHelp.HelpText = paper + covers + "\n\n" +
                                  $"•  Automática: o plugin testa paisagem e retrato e fica com a que dá menos folhas{automatic}.\n" +
                                  "•  Paisagem ou Retrato: todas as folhas nessa orientação.";

            string scaleText = "Escala da impressão: em 1:1000, 1 mm no papel é 1 m do desenho. Pode escrever 1000, 1:1000 ou 1.000.\n\n";
            if (scale is not int sc)
                _scaleHelp.HelpText = scaleText + $"A do desenho é 1:{_drawingScale} (Configuração de Projeto). Vale de 1:{DrawingScale.Min} a 1:{DrawingScale.Max}.";
            else if (sc == _drawingScale)
                _scaleHelp.HelpText = scaleText + "É a escala do desenho (Configuração de Projeto): textos e símbolos saem no tamanho certo na folha.";
            else
                _scaleHelp.HelpText = scaleText + $"Atenção: o desenho está em 1:{_drawingScale}. Em 1:{sc}, textos e símbolos saem " +
                                      (sc > _drawingScale ? $"{sc / (double)_drawingScale:0.#} vezes menores" : $"{_drawingScale / (double)sc:0.#} vezes maiores") +
                                      " na folha. Para acertar, mude a escala em Configuração de Projeto.";
            _scaleHelp.Warning = scale != null && scale != _drawingScale;

            string meters = scale is int m && overlap is double ov ? $" ({viewport.Width * m / 1000 * ov / 100:N0} m nesta folha)" : "";
            _overlapHelp.HelpText = $"Quanto cada folha repete da vizinha nas bordas{meters}, para nada ficar cortado entre uma folha e outra. " +
                                    $"De 0 a {SheetSettings.MaxOverlapPercent:0}%; o padrão é 5%.";

            string name = prefix.Length > 0 ? prefix : "FL";
            _prefixHelp.HelpText = $"As folhas se chamam {name}-01, {name}-02... (uma aba de layout cada); no Model, o quadro das folhas usa os " +
                                   "mesmos nomes." +
                                   (existing > 0 ? $"\n\nAtenção: já existem {existing} folha{(existing == 1 ? "" : "s")} {name}-... neste desenho. " +
                                                   "\"Apagar e gerar novas\" apaga e cria de novo; \"Apagar folhas\" só apaga. Para manter as " +
                                                   "atuais, use outro prefixo." : "");
            _prefixHelp.Warning = existing > 0;
        }

        /// <summary>Folhas demais: diz quantas e sugere a primeira escala comum que dá um número razoável de folhas.</summary>
        private static string TooMany(string count, SheetSettings s)
        {
            int[] common = { 500, 1000, 2000, 2500, 5000, 10000, 20000, 50000 };
            int suggestion = common.FirstOrDefault(c => c > s.Scale && GridCells(new SheetSettings
            {
                Format = s.Format, Orientation = s.Orientation, Scale = c, OverlapPercent = s.OverlapPercent, Area = s.Area
            }) <= ManySheets);
            return $"Isso daria {count} folhas: use uma folha maior" + (suggestion > 0 ? $" ou a escala 1:{suggestion}" : " ou uma escala maior") +
                   ", ou diminua a área.";
        }

        /// <summary>"1000", "1:1000" ou "1.000" → 1000; null se não for uma escala válida.</summary>
        private static int? ParseScale(string text)
        {
            string t = text.Trim();
            if (t.StartsWith("1:", StringComparison.Ordinal)) t = t.Substring(2);
            t = t.Replace(".", "").Replace(" ", "");
            return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int scale) && scale >= DrawingScale.Min && scale <= DrawingScale.Max
                ? scale
                : (int?)null;
        }

        /// <summary>Tamanho da grade (com os pedaços vazios) na orientação que vai ser usada.</summary>
        private static long GridCells(SheetSettings s)
        {
            Rect area = s.Area!.Value;
            double overlap = s.OverlapPercent / 100.0;
            long Cells(bool landscape) => SheetPlanner.GridCells(area, s.SheetFormat, landscape, s.Scale, overlap);
            return s.Orientation == SheetSettings.Automatic ? Math.Min(Cells(true), Cells(false)) : Cells(s.Orientation == SheetSettings.Landscape);
        }

        private static Rect? Extents(IEnumerable<Rect> rects)
        {
            List<Rect> list = rects.ToList();
            if (list.Count == 0) return null;
            return new Rect(list.Min(r => r.MinX), list.Min(r => r.MinY), list.Max(r => r.MaxX), list.Max(r => r.MaxY));
        }

        // ---------- Montagem ----------

        /// <summary>Coluna de linhas de altura fixa (em pixels de 96 dpi); a de altura 0 fica com a sobra.</summary>
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

        /// <summary>Título de seção com o "?" logo depois dele.</summary>
        private Control Section(string text, HelpButton? help)
        {
            var row = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Background };
            row.Controls.Add(new Label
            {
                Text = text.ToUpperInvariant(),
                Font = Theme.Section,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, S(11), S(6), 0)
            });
            if (help != null)
            {
                help.Size = new Size(S(20), S(20));
                help.Margin = new Padding(0, S(7), 0, 0);
                row.Controls.Add(help);
            }
            return row;
        }

        /// <summary>Campos lado a lado, cada um com o "?" à direita da caixa de texto.</summary>
        private TableLayoutPanel Fields(params (LabeledInput Field, HelpButton Help)[] fields)
        {
            var row = new TableLayoutPanel { ColumnCount = 2 * fields.Length, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            int help = S(22);
            // O LabeledInput tem tamanhos fixos (título de 20 e caixa de 28, abaixo de 2 de margem): o "?" no meio da caixa
            int helpTop = 2 + 20 + (28 - help) / 2;
            for (int i = 0; i < fields.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / fields.Length));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(i == fields.Length - 1 ? 24 : 32)));
                var (field, button) = fields[i];
                field.Dock = DockStyle.Fill;
                field.Margin = new Padding(0, 2, S(4), 2);
                button.Size = new Size(help, help);
                button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                button.Margin = new Padding(0, Math.Max(0, helpTop), 0, 0);
                row.Controls.Add(field, 2 * i, 0);
                row.Controls.Add(button, 2 * i + 1, 0);
            }
            return row;
        }
    }

    /// <summary>
    /// Prévia do Gerar Folhas: os elementos do desenho (cinza), a área escolhida (tracejada), as folhas que serão criadas
    /// (azul, com o nome) e os pedaços vazios que ficam de fora (tracejados), com a legenda embaixo.
    /// </summary>
    internal class SheetPreview : Control
    {
        private Rect? _area;
        private IReadOnlyList<Rect> _content = Array.Empty<Rect>();
        private SheetPlan? _plan;
        private string _prefix = "FL";

        public SheetPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Input;
        }

        /// <param name="plan">Divisão a mostrar; null mostra só a área e os elementos.</param>
        public void SetPlan(Rect? area, IReadOnlyList<Rect> content, SheetPlan? plan, string prefix)
        {
            _area = area;
            _content = content;
            _plan = plan;
            _prefix = prefix.Length > 0 ? prefix : "FL";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Input);
            using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            int pad = Theme.Scale(this, 12);
            int legendHeight = Theme.Scale(this, 24);
            var view = new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad - legendHeight);
            DrawLegend(g, new Rectangle(pad, Height - legendHeight - Theme.Scale(this, 4), Width - 2 * pad, legendHeight));
            if (view.Width < 20 || view.Height < 20) return;

            if (_area is not Rect area)
            {
                TextRenderer.DrawText(g, "Escolha a área do projeto para ver como ele fica dividido em folhas", Theme.Small, view, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            // O que aparece: a área e a grade inteira (a grade passa um pouco da área)
            List<Rect> shown = new List<Rect> { area };
            if (_plan != null) shown.AddRange(_plan.Tiles.Concat(_plan.Skipped).Select(t => t.ModelArea));
            double minX = shown.Min(r => r.MinX), minY = shown.Min(r => r.MinY), maxX = shown.Max(r => r.MaxX), maxY = shown.Max(r => r.MaxY);
            var world = new Rect(minX, minY, maxX, maxY);
            double k = Math.Min(view.Width / Math.Max(world.Width, 1e-9), view.Height / Math.Max(world.Height, 1e-9));
            float offsetX = (float)(view.X + (view.Width - world.Width * k) / 2), offsetY = (float)(view.Y + (view.Height - world.Height * k) / 2);
            RectangleF R(Rect r) => RectangleF.FromLTRB(offsetX + (float)((r.MinX - minX) * k), offsetY + (float)((maxY - r.MaxY) * k),
                                                        offsetX + (float)((r.MaxX - minX) * k), offsetY + (float)((maxY - r.MinY) * k));

            g.SmoothingMode = SmoothingMode.None;
            // Elementos do desenho: os pequenos como pontos, os grandes só com o contorno
            using (var dot = new SolidBrush(Color.FromArgb(170, Theme.Muted)))
            using (var outline = new Pen(Color.FromArgb(60, Theme.Muted)))
            {
                foreach (Rect c in _content)
                {
                    if (!c.Intersects(world)) continue;
                    RectangleF r = R(c);
                    if (r.Width < 3 && r.Height < 3) g.FillRectangle(dot, r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
                    else g.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height);
                }
            }

            if (_plan != null)
            {
                using (var empty = new Pen(Theme.Muted) { DashStyle = DashStyle.Dash })
                {
                    foreach (SheetTile tile in _plan.Skipped)
                    {
                        RectangleF r = R(tile.ModelArea);
                        g.DrawRectangle(empty, r.X, r.Y, r.Width, r.Height);
                    }
                }

                int digits = _plan.Tiles.Count >= 100 ? 3 : 2;
                using (var fill = new SolidBrush(Color.FromArgb(45, Theme.Accent)))
                using (var pen = new Pen(Theme.Accent, Theme.Scale(this, 1) * 1.5f))
                {
                    for (int i = 0; i < _plan.Tiles.Count; i++)
                    {
                        RectangleF r = R(_plan.Tiles[i].ModelArea);
                        g.FillRectangle(fill, r);
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

                        // Nome da folha no meio; sem espaço, só o número
                        string number = (i + 1).ToString("D" + digits, CultureInfo.InvariantCulture);
                        string label = $"{_prefix}-{number}";
                        Size size = TextRenderer.MeasureText(label, Theme.Small);
                        if (size.Width > r.Width - 4) { label = (i + 1).ToString(CultureInfo.InvariantCulture); size = TextRenderer.MeasureText(label, Theme.Small); }
                        if (size.Width > r.Width - 2 || size.Height > r.Height - 2) continue;
                        TextRenderer.DrawText(g, label, Theme.Small, Rectangle.Round(r), Theme.TextStrong,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    }
                }
            }

            using (var areaPen = new Pen(Theme.TextStrong, Theme.Scale(this, 1) * 1.5f) { DashStyle = DashStyle.Dash })
            {
                RectangleF r = R(area);
                g.DrawRectangle(areaPen, r.X, r.Y, r.Width, r.Height);
            }
        }

        /// <summary>Legenda numa linha: folha, pedaço vazio, área e elementos do desenho (o que não couber fica de fora).</summary>
        private void DrawLegend(Graphics g, Rectangle where)
        {
            int box = Theme.Scale(this, 12), gap = Theme.Scale(this, 6), space = Theme.Scale(this, 16);
            int x = where.X, y = where.Y + (where.Height - box) / 2;
            var items = new (string Text, Action<Rectangle> Draw)[]
            {
                ("Folha", r =>
                {
                    using (var fill = new SolidBrush(Color.FromArgb(45, Theme.Accent))) g.FillRectangle(fill, r);
                    using (var pen = new Pen(Theme.Accent, 1.5f)) g.DrawRectangle(pen, r);
                }),
                ("Pedaço vazio (não vira folha)", r => { using (var pen = new Pen(Theme.Muted) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r); }),
                ("Área escolhida", r => { using (var pen = new Pen(Theme.TextStrong, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, r.Left, r.Top + r.Height / 2, r.Right, r.Top + r.Height / 2); }),
                ("Elementos do desenho", r => { using (var dot = new SolidBrush(Theme.Muted)) g.FillRectangle(dot, r.Left + r.Width / 2 - 2, r.Top + r.Height / 2 - 2, 4, 4); })
            };
            foreach (var (text, draw) in items)
            {
                Size size = TextRenderer.MeasureText(text, Theme.Small);
                if (x + box + gap + size.Width > where.Right) break;
                draw(new Rectangle(x, y, box, box));
                TextRenderer.DrawText(g, text, Theme.Small, new Point(x + box + gap, where.Y + (where.Height - size.Height) / 2), Theme.Muted, TextFormatFlags.NoPrefix);
                x += box + gap + size.Width + space;
            }
        }
    }
}
