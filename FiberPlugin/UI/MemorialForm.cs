
namespace FiberPlugin.UI
{
    /// <summary>
    /// Dados do projeto para o Memorial Descritivo: percurso, endereço, local e data, contrato de uso mútuo,
    /// ART, início e prazo da obra. Os números (postes, cabos, esforços) vêm do desenho e aparecem no cabeçalho.
    /// </summary>
    internal class MemorialForm : Form
    {
        private readonly TableLayoutPanel _body;
        private readonly InputBox _route;
        private readonly InputBox _address;
        private readonly InputBox _placeDate;
        private readonly InputBox _contract;
        private readonly InputBox _art;
        private readonly InputBox _start;
        private readonly InputBox _deadline;
        private readonly ThemedButton _ok;

        public string Route => _route.Query;
        public string WorkAddress => _address.Query;
        public string PlaceAndDate => _placeDate.Query;
        public string ContractNumber => _contract.Query;
        public string ArtNumber => _art.Query;
        public string StartDate => _start.Query;
        public string Deadline => _deadline.Query;

        /// <param name="summary">Resumo do desenho, ex.: "223 postes · 8.999 m de cabo · 18 CTO".</param>
        public MemorialForm(string summary, string route, string address, string placeAndDate,
            string contract, string art, string start, string deadline)
        {
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Memorial Descritivo";
            ClientSize = new Size(600, 500);

            var header = new HeaderPanel { IconCommand = "FIBRA_MEMORIAL", Title = "Memorial Descritivo", Subtitle = summary };

            _body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(18, 6, 18, 8),
                BackColor = Theme.Background
            };
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _route = Field("Percurso da rede (também vai na plaqueta)", "Ex.: Nova Califórnia – Porto Velho/RO", route, 0, 2);
            _address = Field("Endereço da obra", "Ex.: Localizado no distrito de Nova Califórnia, Porto Velho/RO", address, 0, 2);
            _placeDate = Field("Local e data", "Ex.: Porto Velho/RO, 29 de setembro de 2026", placeAndDate, 0, 2);
            _contract = Field("Contrato de uso mútuo nº", "Obrigatório na NDU 009", contract, 0, 1);
            _art = Field("ART nº", "Anotação de responsabilidade técnica", art, 1, 1);
            _start = Field("Início previsto da obra", "Ex.: 01/11/2026", start, 0, 1);
            _deadline = Field("Prazo de execução", "Ex.: 60 dias", deadline, 1, 1);

            var footer = new FooterPanel { Hint = "Empresa: Pasta de Dados > empresa.txt" };
            _ok = new ThemedButton("Gerar PDF", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(_ok);
            footer.AddButton(cancel);

            Controls.Add(_body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            // O percurso é obrigatório: aparece na capa, no cabeçalho e na plaqueta
            _route.Input.TextChanged += (s, e) => _ok.Enabled = Route.Length > 0;
            _ok.Enabled = Route.Length > 0;
            _ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Shown += (s, e) => _route.Input.Focus();
        }

        /// <param name="column">Coluna (0 ou 1); com <paramref name="span"/> 2 o campo ocupa a largura toda.</param>
        private InputBox Field(string label, string placeholder, string value, int column, int span)
        {
            // Um campo que começa na coluna 0 abre duas linhas novas (título e campo)
            if (column == 0) _body.RowCount += 2;
            int row = _body.RowCount - 2;

            var title = new Label
            {
                Text = label.ToUpperInvariant(),
                Font = Theme.Section,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(column == 0 ? 0 : 6, 10, 0, 4)
            };
            var box = new InputBox(placeholder, searchIcon: false)
            {
                Dock = DockStyle.Top,
                Margin = new Padding(column == 0 ? 0 : 6, 0, column == 0 && span == 1 ? 6 : 0, 0)
            };
            box.Input.Text = value;

            _body.Controls.Add(title, column, row);
            _body.Controls.Add(box, column, row + 1);
            _body.SetColumnSpan(title, span);
            _body.SetColumnSpan(box, span);
            return box;
        }
    }
}
