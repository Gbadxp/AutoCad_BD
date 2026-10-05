using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Aba Projeto da janela Configurações (era a janela Dados do Projeto): percurso, endereço, contrato, ART, início e
    /// prazo (usados nos documentos), zona UTM e escala deste desenho, e os atalhos para a pasta de dados e o Atualizar
    /// Blocos. Confere enquanto o usuário digita (Error); quem grava e atualiza o desenho é o comando, depois do Salvar.
    /// </summary>
    internal class ProjectPanel : TableLayoutPanel
    {
        private static readonly int[] Scales = { 500, 1000, 2000 };

        private readonly bool _drawingHasZone;
        private readonly bool _zoneFromFile;
        private readonly LabeledInput _route, _address, _contract, _art, _start, _deadline;
        private readonly InputBox _zone;
        private readonly ChoiceBar _hemisphere;
        private readonly InputBox _scale;
        private readonly ChoiceBar _scalePresets;
        private readonly Label _filesHint;
        private bool _syncing;

        /// <summary>Algum campo mudou (o resultado e o Error já estão atualizados).</summary>
        public event EventHandler? Changed;

        /// <summary>O usuário clicou em Atualizar Blocos: a janela salva e o comando roda o FIBRA_ATUALIZAR_BLOCOS depois.</summary>
        public event EventHandler? UpdateBlocksClicked;

        /// <summary>Abre a pasta de dados no Explorer; devolve a pasta ou null se ela não existir.</summary>
        public Func<string?>? OpenDataFolder { get; set; }

        /// <summary>Dados digitados (valem enquanto Error for null).</summary>
        public ProjectInfo Info { get; private set; } = new ProjectInfo();

        /// <summary>Escala 1:X digitada.</summary>
        public int ScaleDenominator { get; private set; }

        /// <summary>O que está errado na aba (null se estiver tudo certo).</summary>
        public string? Error { get; private set; }

        /// <summary>"Zona UTM 20 Sul · escala 1:1.000", para a linha de status.</summary>
        public string Summary { get; private set; } = "";

        /// <param name="drawingHasZone">O desenho já tem zona: ela pode mudar, mas não ficar vazia.</param>
        public ProjectPanel(ProjectInfo info, int scale, bool drawingHasZone)
        {
            _drawingHasZone = drawingHasZone;
            _zoneFromFile = info.ZoneFromFile;
            ColumnCount = 1;
            BackColor = Theme.Background;
            Padding = new Padding(0, 4, 0, 0);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _route = new LabeledInput("Percurso da rede (também vai na plaqueta)", "Ex.: Nova Califórnia – Porto Velho/RO");
            _address = new LabeledInput("Endereço da obra", "Ex.: Localizado no distrito de Nova Califórnia, Porto Velho/RO");
            _contract = new LabeledInput("Contrato de uso mútuo nº", "Obrigatório na NDU 009");
            _art = new LabeledInput("ART nº", "Anotação de responsabilidade técnica");
            _start = new LabeledInput("Início previsto da obra", "Ex.: 01/11/2026");
            _deadline = new LabeledInput("Prazo de execução", "Ex.: 60 dias");

            _zone = new InputBox("20", searchIcon: false) { Size = new Size(56, 28), Margin = new Padding(0, 4, 6, 0) };
            _hemisphere = new ChoiceBar("Sul", "Norte") { Margin = new Padding(0, 4, 0, 0) };
            _scale = new InputBox("1000", searchIcon: false) { Size = new Size(80, 28), Margin = new Padding(0, 4, 6, 0) };
            _scalePresets = new ChoiceBar(Scales.Select(s => "1:" + s.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))).ToArray()) { Margin = new Padding(0, 4, 0, 0) };

            var folder = new ThemedButton("Pasta de Dados", false) { Margin = new Padding(0, 4, 8, 0) };
            var blocks = new ThemedButton("Atualizar Blocos", false) { Margin = new Padding(0, 4, 8, 0) };
            _filesHint = Hint("Logo e figuras do memorial e BLOCOS.dwg");

            foreach (var (control, height) in new (Control, float)[]
            {
                (new SectionLabel("Projeto deste desenho"), 26), (_route, 54), (_address, 54),
                (Pair(_contract, _art), 54), (Pair(_start, _deadline), 54),
                (new SectionLabel("Desenho"), 32), (Row("Zona UTM", _zone, _hemisphere), 40), (Row("Escala   1:", _scale, _scalePresets), 40),
                (new SectionLabel("Arquivos do plugin"), 32), (Row("", folder, blocks, _filesHint), 40),
                (SmallHint("Ficam no desenho e também em Documentos\\Fiber Plugin\\projeto.txt: o próximo desenho já abre com eles. " +
                           "Ao salvar com zona ou escala nova, o plugin pergunta se atualiza os postes e as anotações já desenhados."), 40)
            })
            {
                RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                control.Dock = DockStyle.Fill;
                control.Margin = Padding.Empty;
                Controls.Add(control, 0, RowCount++);
            }
            // Sobra de altura numa linha vazia no fim (senão a última linha estica)
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            RowCount++;

            // ---------- Valores iniciais ----------
            _route.Value = info.Route;
            _address.Value = info.WorkAddress;
            _contract.Value = info.ContractNumber;
            _art.Value = info.ArtNumber;
            _start.Value = info.StartDate;
            _deadline.Value = info.Deadline;
            if (info.Zone != null)
            {
                _zone.Input.Text = info.Zone.Zone.ToString(CultureInfo.InvariantCulture);
                _hemisphere.SelectedIndex = info.Zone.South ? 0 : 1;
            }
            _scale.Input.Text = scale.ToString(CultureInfo.InvariantCulture);
            SyncScalePresets();
            Check();

            // ---------- Eventos ----------
            foreach (LabeledInput input in new[] { _route, _address, _contract, _art, _start, _deadline }) input.Box.Input.TextChanged += (s, e) => Check();
            foreach (InputBox box in new[] { _zone, _scale }) box.Input.TextChanged += (s, e) => Check();
            _scale.Input.TextChanged += (s, e) => SyncScalePresets();
            _hemisphere.SelectedChanged += (s, e) => Check();
            _scalePresets.SelectedChanged += (s, e) =>
            {
                if (_syncing || _scalePresets.SelectedIndex < 0) return;
                _scale.Input.Text = Scales[_scalePresets.SelectedIndex].ToString(CultureInfo.InvariantCulture);
            };
            folder.Click += (s, e) =>
            {
                string? dir = OpenDataFolder?.Invoke();
                _filesHint.Text = dir != null ? "Pasta aberta: " + dir : "Não foi possível abrir a pasta de dados do plugin.";
                _filesHint.ForeColor = dir != null ? Theme.Muted : Theme.Error;
            };
            blocks.Click += (s, e) => UpdateBlocksClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Cursor no primeiro campo (quando a janela abre nesta aba).</summary>
        public void FocusFirst() => _route.Box.Input.Focus();

        /// <summary>Confere zona e escala e monta o resultado.</summary>
        private void Check()
        {
            Error = Read(out UtmSettings? zone, out int scale);
            if (Error == null)
            {
                Info = new ProjectInfo
                {
                    Route = _route.Value, WorkAddress = _address.Value, ContractNumber = _contract.Value,
                    ArtNumber = _art.Value, StartDate = _start.Value, Deadline = _deadline.Value, Zone = zone
                };
                ScaleDenominator = scale;
                Summary = (zone == null ? "sem zona UTM" : $"zona {zone.Zone} {(zone.South ? "Sul" : "Norte")}" + (_zoneFromFile ? " (do último projeto)" : "")) +
                          $" · 1:{scale.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))}";
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private string? Read(out UtmSettings? zone, out int scale)
        {
            zone = null;
            scale = 0;
            string zoneText = _zone.Query;
            if (zoneText.Length > 0)
            {
                if (!int.TryParse(zoneText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) || z < 1 || z > 60)
                    return "Projeto: zona UTM inválida, use um número de 1 a 60 (ex.: 20, 22, 23).";
                zone = new UtmSettings { Zone = z, South = _hemisphere.SelectedIndex == 0 };
            }
            else if (_drawingHasZone)
            {
                return "Projeto: este desenho já tem zona UTM; ela pode mudar, mas não ficar vazia.";
            }

            if (!int.TryParse(_scale.Query.Replace(".", "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) ||
                scale < DrawingScale.Min || scale > DrawingScale.Max)
            {
                return $"Projeto: escala inválida, use 1:X com X de {DrawingScale.Min} a {DrawingScale.Max:N0} (ex.: 1000).";
            }
            return null;
        }

        /// <summary>Marca o atalho de escala que corresponde ao valor digitado (nenhum se for outro valor).</summary>
        private void SyncScalePresets()
        {
            _syncing = true;
            _scalePresets.SelectedIndex = int.TryParse(_scale.Query, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
                ? Array.IndexOf(Scales, s)
                : -1;
            _syncing = false;
        }

        // ---------- Montagem ----------

        private static Label Hint(string text) => new Label
        {
            Text = text,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = true,
            Margin = new Padding(4, 11, 0, 0)
        };

        private static Label SmallHint(string text) => new Label
        {
            Text = text,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = false
        };

        /// <summary>Dois campos lado a lado, em colunas iguais.</summary>
        private static TableLayoutPanel Pair(LabeledInput left, LabeledInput right)
        {
            var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.Dock = right.Dock = DockStyle.Fill;
            left.Margin = new Padding(0, 0, 6, 0);
            right.Margin = new Padding(6, 0, 0, 0);
            grid.Controls.Add(left, 0, 0);
            grid.Controls.Add(right, 1, 0);
            return grid;
        }

        /// <summary>Linha "rótulo | controles" com o rótulo numa coluna fixa.</summary>
        private static TableLayoutPanel Row(string label, params Control[] controls)
        {
            var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // A linha ocupa só a altura da grade (sem isso ela cresce e o rótulo centralizado sai da área visível)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.Controls.Add(new Label { Text = label, ForeColor = Theme.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
            flow.Controls.AddRange(controls);
            grid.Controls.Add(flow, 1, 0);
            return grid;
        }
    }
}
