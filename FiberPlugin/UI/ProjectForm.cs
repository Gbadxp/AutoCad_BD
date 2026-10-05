using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Dados do projeto num lugar só: percurso, endereço, contrato, ART, início e prazo (usados nos documentos), zona
    /// UTM e escala do desenho, e os atalhos para a pasta de dados e o Atualizar Blocos. Confere enquanto o usuário
    /// digita; quem grava e atualiza o desenho é o comando, depois do Salvar.
    /// </summary>
    internal class ProjectForm : Form
    {
        private static readonly int[] Scales = { 500, 1000, 2000 };

        private readonly bool _drawingHasZone;
        private readonly LabeledInput _route, _address, _contract, _art, _start, _deadline;
        private readonly InputBox _zone;
        private readonly ChoiceBar _hemisphere;
        private readonly InputBox _scale;
        private readonly ChoiceBar _scalePresets;
        private readonly StatusLabel _status;
        private readonly ThemedButton _ok;
        private readonly bool _zoneFromFile;
        private bool _syncing;

        /// <summary>Abre a pasta de dados no Explorer; devolve a pasta ou null se ela não existir.</summary>
        public Func<string?>? OpenDataFolder { get; set; }

        /// <summary>Dados digitados (válidos depois do OK).</summary>
        public ProjectInfo Info { get; private set; } = new ProjectInfo();

        /// <summary>Escala 1:X digitada.</summary>
        public int ScaleDenominator { get; private set; }

        /// <summary>O usuário clicou em Atualizar Blocos: o comando salva e depois roda o FIBRA_ATUALIZAR_BLOCOS.</summary>
        public bool UpdateBlocksAfter { get; private set; }

        /// <param name="drawingHasZone">O desenho já tem zona: ela pode mudar, mas não ficar vazia.</param>
        public ProjectForm(ProjectInfo info, int scale, bool drawingHasZone)
        {
            _drawingHasZone = drawingHasZone;

            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Dados do Projeto";
            ClientSize = new Size(640, 610);

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_DADOS_PROJETO",
                Title = "Dados do Projeto",
                Subtitle = "Usados nos documentos e nos comandos do desenho"
            };

            _route = new LabeledInput("Percurso da rede (também vai na plaqueta)", "Ex.: Nova Califórnia – Porto Velho/RO") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _address = new LabeledInput("Endereço da obra", "Ex.: Localizado no distrito de Nova Califórnia, Porto Velho/RO") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _contract = new LabeledInput("Contrato de uso mútuo nº", "Obrigatório na NDU 009");
            _art = new LabeledInput("ART nº", "Anotação de responsabilidade técnica");
            _start = new LabeledInput("Início previsto da obra", "Ex.: 01/11/2026");
            _deadline = new LabeledInput("Prazo de execução", "Ex.: 60 dias");

            _zone = new InputBox("20", searchIcon: false) { Size = new Size(56, 28), Margin = new Padding(0, 4, 6, 0) };
            _hemisphere = new ChoiceBar("Sul", "Norte") { Margin = new Padding(0, 4, 0, 0) };
            _scale = new InputBox("1000", searchIcon: false) { Size = new Size(80, 28), Margin = new Padding(0, 4, 6, 0) };
            _scalePresets = new ChoiceBar(Scales.Select(s => "1:" + s.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))).ToArray()) { Margin = new Padding(0, 4, 0, 0) };

            var zoneRow = Row("Zona UTM", _zone, _hemisphere);
            var scaleRow = Row("Escala   1:", _scale, _scalePresets);

            var folder = new ThemedButton("Pasta de Dados", false) { Margin = new Padding(0, 4, 8, 0) };
            var blocks = new ThemedButton("Atualizar Blocos", false) { Margin = new Padding(0, 4, 8, 0) };
            var filesRow = Row("", folder, blocks, Hint("Logo e figuras do memorial e BLOCOS.dwg"));

            _status = new StatusLabel { Dock = DockStyle.Fill };

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 4, 18, 6), BackColor = Theme.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (control, height) in new (Control, float)[]
            {
                (new SectionLabel("Projeto"), 26), (_route, 54), (_address, 54), (Pair(_contract, _art), 54), (Pair(_start, _deadline), 54),
                (new SectionLabel("Desenho"), 32), (zoneRow, 40), (scaleRow, 40),
                (new SectionLabel("Arquivos do plugin"), 32), (filesRow, 40),
                (_status, 40)
            })
            {
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                control.Dock = DockStyle.Fill;
                body.Controls.Add(control, 0, body.RowCount++);
            }
            // Sobra de altura numa linha vazia no fim (senão a última linha estica)
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowCount++;

            var footer = new FooterPanel { Hint = "Os dados também ficam salvos para os próximos desenhos" };
            _ok = new ThemedButton("Salvar", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(_ok);
            footer.AddButton(cancel);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

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
            _zoneFromFile = info.ZoneFromFile;
            _scale.Input.Text = scale.ToString(CultureInfo.InvariantCulture);
            SyncScalePresets();

            // ---------- Eventos ----------
            foreach (InputBox box in new[] { _zone, _scale }) box.Input.TextChanged += (s, e) => UpdateStatus();
            _scale.Input.TextChanged += (s, e) => SyncScalePresets();
            _hemisphere.SelectedChanged += (s, e) => UpdateStatus();
            _scalePresets.SelectedChanged += (s, e) =>
            {
                if (_syncing || _scalePresets.SelectedIndex < 0) return;
                _scale.Input.Text = Scales[_scalePresets.SelectedIndex].ToString(CultureInfo.InvariantCulture);
            };
            folder.Click += (s, e) =>
            {
                string? dir = OpenDataFolder?.Invoke();
                _status.Set(dir != null ? "Pasta aberta: " + dir : "Não foi possível abrir a pasta de dados do plugin.", dir != null ? StatusKind.Ok : StatusKind.Error);
            };
            blocks.Click += (s, e) =>
            {
                if (!Accept()) return;
                UpdateBlocksAfter = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            _ok.Click += (s, e) =>
            {
                if (!Accept()) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (s, e) => _route.Box.Input.Focus();

            UpdateStatus();
        }

        /// <summary>Confere zona e escala e monta o resultado. False (com o motivo na linha de status) se algo estiver errado.</summary>
        private bool Accept()
        {
            if (!TryRead(out UtmSettings? zone, out int scale)) return false;
            Info = new ProjectInfo
            {
                Route = _route.Value, WorkAddress = _address.Value, ContractNumber = _contract.Value,
                ArtNumber = _art.Value, StartDate = _start.Value, Deadline = _deadline.Value, Zone = zone
            };
            ScaleDenominator = scale;
            return true;
        }

        private bool TryRead(out UtmSettings? zone, out int scale)
        {
            zone = null;
            scale = 0;
            string zoneText = _zone.Query;
            if (zoneText.Length > 0)
            {
                if (!int.TryParse(zoneText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) || z < 1 || z > 60)
                {
                    _status.Set("Zona UTM inválida: use um número de 1 a 60 (ex.: 20, 22, 23).", StatusKind.Error);
                    return false;
                }
                zone = new UtmSettings { Zone = z, South = _hemisphere.SelectedIndex == 0 };
            }
            else if (_drawingHasZone)
            {
                _status.Set("Este desenho já tem zona UTM: ela pode mudar, mas não ficar vazia.", StatusKind.Error);
                return false;
            }

            if (!int.TryParse(_scale.Query.Replace(".", "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) ||
                scale < DrawingScale.Min || scale > DrawingScale.Max)
            {
                _status.Set($"Escala inválida: use 1:X com X de {DrawingScale.Min} a {DrawingScale.Max:N0} (ex.: 1000).", StatusKind.Error);
                return false;
            }
            return true;
        }

        private void UpdateStatus()
        {
            bool valid = TryRead(out UtmSettings? zone, out int scale);
            _ok.Enabled = valid;
            if (!valid) return;
            string zoneText = zone == null ? "Sem zona UTM"
                : $"Zona UTM {zone.Zone} {(zone.South ? "Sul" : "Norte")}" + (_zoneFromFile ? ", sugerida pelo último projeto" : "");
            _status.Set($"{zoneText}  ·  escala 1:{scale.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))}", StatusKind.Ok);
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
