using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>Escolha do modelo de poste (DT/CC, altura/esforço) para o FIBRA_INSERIR_POSTE.</summary>
    public class PoleSelectionForm : Form
    {
        private readonly List<PoleData> allModels;
        private readonly SearchBox search;
        private readonly ThemedListBox list;
        private readonly Label emptyLabel;
        private readonly ThemedButton btnOk;

        public PoleData? SelectedModel { get; private set; }

        public PoleSelectionForm(List<PoleData> models, PoleData? current = null)
        {
            allModels = models;

            Theme.ApplyForm(this);
            this.Text = "Fiber Plugin - Inserir Postes";
            this.ClientSize = new Size(480, 500);

            var header = new HeaderPanel
            {
                Glyph = Theme.Icons.Pin,
                IconCommand = "FIBRA_INSERIR_POSTE",
                Title = "Modelo do poste",
                Subtitle = $"{models.Count} modelo(s) cadastrados em Dados\\postes.csv"
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 10, 16, 8), BackColor = Theme.Background };
            search = new SearchBox("Buscar (ex.: 11/300, CC, DT)...") { Dock = DockStyle.Fill };
            toolbar.Controls.Add(search);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 12), BackColor = Theme.Background };
            list = new ThemedListBox
            {
                Dock = DockStyle.Fill,
                LogicalItemHeight = 42,
                Describe = o =>
                {
                    var m = (PoleData)o;
                    string details = $"{m.TypeName}  ·  {m.HeightM.ToString("0.#", CultureInfo.CurrentCulture)} m  ·  " +
                                     $"{m.EffortDaN.ToString("0", CultureInfo.CurrentCulture)} daN";
                    return (m.Designation, details, m.Type);
                }
            };
            emptyLabel = new Label
            {
                Text = "Nenhum modelo encontrado",
                ForeColor = Theme.Muted,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 60,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            body.Controls.Add(list);
            body.Controls.Add(emptyLabel);

            var footer = new FooterPanel { Hint = "Enter ou duplo clique confirma" };
            btnOk = new ThemedButton("Inserir", true);
            var btnCancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(btnOk);
            footer.AddButton(btnCancel);

            this.Controls.Add(body);
            this.Controls.Add(toolbar);
            this.Controls.Add(header);
            this.Controls.Add(footer);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            btnOk.Click += (s, e) => Accept();
            list.DoubleClick += (s, e) => Accept();
            search.Input.TextChanged += (s, e) => ApplyFilter();
            search.DriveList(list);

            ApplyFilter();

            // Já deixa selecionado o modelo em uso
            if (current != null)
            {
                PoleData? match = allModels.FirstOrDefault(m => m.Designation == current.Designation);
                if (match != null) list.SelectedItem = match;
            }

            this.Shown += (s, e) => search.Input.Focus();
        }

        private void ApplyFilter()
        {
            string query = search.Query;
            bool Matches(PoleData m) => SearchBox.Matches(m.Designation, query) || SearchBox.Matches(m.TypeName, query);

            list.BeginUpdate();
            list.Items.Clear();
            foreach (PoleData m in allModels.Where(Matches)) list.Items.Add(m);
            list.EndUpdate();

            if (list.Items.Count > 0) list.SelectedIndex = 0;
            emptyLabel.Visible = list.Items.Count == 0;
            btnOk.Enabled = list.Items.Count > 0;
        }

        private void Accept()
        {
            if (list.SelectedItem is not PoleData model) return;
            SelectedModel = model;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
