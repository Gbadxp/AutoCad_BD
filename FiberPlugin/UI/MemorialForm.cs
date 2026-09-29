using System.Windows.Forms;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Dados do projeto para o Memorial Descritivo (percurso, endereço da obra, local e data).
    /// Os números (postes, cabos, CTO/CEO) vêm do desenho e aparecem no cabeçalho.
    /// </summary>
    internal class MemorialForm : Form
    {
        private readonly InputBox _route;
        private readonly InputBox _address;
        private readonly InputBox _placeDate;
        private readonly ThemedButton _ok;

        public string Route => _route.Query;
        public string WorkAddress => _address.Query;
        public string PlaceAndDate => _placeDate.Query;

        /// <param name="summary">Resumo do desenho, ex.: "223 postes · 8.999 m de cabo · 18 CTO".</param>
        public MemorialForm(string summary, string route, string address, string placeAndDate)
        {
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Memorial Descritivo";
            ClientSize = new Size(560, 340);

            var header = new HeaderPanel { IconCommand = "FIBRA_MEMORIAL", Title = "Memorial Descritivo", Subtitle = summary };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(18, 12, 18, 8),
                BackColor = Theme.Background
            };
            _route = Field(body, "Percurso da rede (também vai na plaqueta)", "Ex.: Nova Califórnia – Porto Velho/RO", route);
            _address = Field(body, "Endereço da obra", "Ex.: Localizado no distrito de Nova Califórnia, Porto Velho/RO", address);
            _placeDate = Field(body, "Local e data", "Ex.: Porto Velho/RO, 29 de setembro de 2026", placeAndDate);

            var footer = new FooterPanel { Hint = "Empresa: Pasta de Dados > empresa.txt" };
            _ok = new ThemedButton("Gerar PDF", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(_ok);
            footer.AddButton(cancel);

            Controls.Add(body);
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

        private static InputBox Field(TableLayoutPanel body, string label, string placeholder, string value)
        {
            body.Controls.Add(new Label
            {
                Text = label.ToUpperInvariant(),
                Font = Theme.Section,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 4)
            });

            var box = new InputBox(placeholder, searchIcon: false) { Dock = DockStyle.Top, Margin = new Padding(0) };
            box.Input.Text = value;
            body.Controls.Add(box);
            return box;
        }
    }
}
