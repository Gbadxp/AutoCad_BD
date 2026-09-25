using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FiberPlugin.Models;

namespace FiberPlugin.UI
{
    public class CableSelectionForm : Form
    {
        private readonly List<CableModel> allCables;
        private readonly SearchBox search;
        private readonly ThemedListBox list;
        private readonly Label emptyLabel;
        private readonly ThemedButton btnOk;

        public CableModel? SelectedCable { get; private set; }

        public CableSelectionForm(List<CableModel> cables, string buttonText = "OK")
        {
            allCables = cables;

            Theme.ApplyForm(this);
            this.Text = "Fiber Plugin - Selecionar Cabo";
            this.ClientSize = new Size(500, 480);

            var header = new HeaderPanel
            {
                Glyph = Theme.Icons.Fiber,
                Title = "Selecionar cabo",
                Subtitle = $"{cables.Count} cabo(s) cadastrados em Dados\\cabos.csv"
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 10, 16, 8), BackColor = Theme.Background };
            search = new SearchBox("Buscar por nome, tipo ou peso...") { Dock = DockStyle.Fill };
            toolbar.Controls.Add(search);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 12), BackColor = Theme.Background };
            list = new ThemedListBox
            {
                Dock = DockStyle.Fill,
                LogicalItemHeight = 44,
                Describe = o =>
                {
                    var c = (CableModel)o;
                    return (c.FullName, c.ShortName, $"{c.WeightKgKm.ToString("0.##", CultureInfo.CurrentCulture)} kg/km");
                }
            };
            emptyLabel = new Label
            {
                Text = "Nenhum cabo encontrado",
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
            btnOk = new ThemedButton(buttonText, true);
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
            this.Shown += (s, e) => search.Input.Focus();
        }

        private void ApplyFilter()
        {
            string query = search.Query;
            bool Matches(string text) => SearchBox.Matches(text, query);

            object? previous = list.SelectedItem;

            list.BeginUpdate();
            list.Items.Clear();
            foreach (CableModel c in allCables.Where(c => Matches(c.FullName) || Matches(c.ShortName) || Matches(c.WeightKgKm.ToString(CultureInfo.CurrentCulture))))
            {
                list.Items.Add(c);
            }
            list.EndUpdate();

            if (list.Items.Count > 0)
                list.SelectedIndex = previous != null && list.Items.Contains(previous) ? list.Items.IndexOf(previous) : 0;

            emptyLabel.Visible = list.Items.Count == 0;
            btnOk.Enabled = list.Items.Count > 0;
        }

        private void Accept()
        {
            if (list.SelectedItem is not CableModel cable) return;
            SelectedCable = cable;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
