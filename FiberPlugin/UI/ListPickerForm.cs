using System.Globalization;
using FiberPlugin.Core;
using FiberPlugin.Models;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Janela de escolha com busca e lista (cabo, modelo de poste, bloco): cabeçalho, campo de busca,
    /// lista filtrada e botões. Enter ou duplo clique confirma.
    /// </summary>
    internal class ListPickerForm<T> : Form where T : class
    {
        private readonly List<T> _items;
        private readonly Func<T, string, bool> _matches;
        private readonly Label _empty;
        private readonly ThemedButton _ok;

        protected SearchBox Search { get; }
        protected ThemedListBox List { get; }
        protected Panel Body { get; }

        /// <summary>Item escolhido (null se o usuário cancelou).</summary>
        public T? Selected { get; private set; }

        /// <param name="heading">Título da janela, também mostrado no cabeçalho.</param>
        /// <param name="iconCommand">Comando cujo ícone aparece no cabeçalho.</param>
        /// <param name="describe">Linha principal, linha secundária e etiqueta à direita de cada item.</param>
        /// <param name="matches">Se o item corresponde ao texto buscado.</param>
        public ListPickerForm(List<T> items, string heading, string iconCommand, string subtitle, string searchHint,
            string emptyText, string okText, Func<T, (string, string?, string?)> describe, Func<T, string, bool> matches,
            int itemHeight = 42, string hint = "Enter ou duplo clique confirma")
        {
            _items = items;
            _matches = matches;

            Theme.ApplyForm(this);
            Text = "Fiber Plugin - " + heading;
            ClientSize = new Size(480, 500);

            var header = new HeaderPanel { IconCommand = iconCommand, Title = heading, Subtitle = subtitle };
            Search = new SearchBox(searchHint);

            List = new ThemedListBox { Dock = DockStyle.Fill, LogicalItemHeight = itemHeight, Describe = o => describe((T)o) };
            _empty = new Label
            {
                Text = emptyText,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 60,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            var listPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            listPanel.Controls.Add(List);
            listPanel.Controls.Add(_empty);

            Body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 12), BackColor = Theme.Background };
            Body.Controls.Add(listPanel);

            var footer = new FooterPanel { Hint = hint };
            _ok = new ThemedButton(okText, true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(_ok);
            footer.AddButton(cancel);

            // Ordem de encaixe: o último adicionado encaixa primeiro
            Controls.Add(Body);
            Controls.Add(Search.InToolbar());
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            _ok.Click += (s, e) => Accept();
            List.DoubleClick += (s, e) => Accept();
            Search.Input.TextChanged += (s, e) => ApplyFilter();
            Search.DriveList(List);
            Shown += (s, e) => Search.Input.Focus();

            ApplyFilter();
        }

        public void SelectItem(T item) => List.SelectedItem = item;

        /// <summary>Filtro extra além da busca (ex.: categoria dos blocos).</summary>
        protected virtual bool Include(T item) => true;

        protected void ApplyFilter()
        {
            string query = Search.Query;
            object? previous = List.SelectedItem;

            List.BeginUpdate();
            List.Items.Clear();
            foreach (T item in _items.Where(i => Include(i) && _matches(i, query))) List.Items.Add(item);
            List.EndUpdate();

            if (List.Items.Count > 0)
                List.SelectedIndex = previous != null && List.Items.Contains(previous) ? List.Items.IndexOf(previous) : 0;
            _empty.Visible = List.Items.Count == 0;
            _ok.Enabled = List.Items.Count > 0;
        }

        private void Accept()
        {
            if (List.SelectedItem is not T item) return;
            Selected = item;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Janelas de escolha de cabo e de modelo de poste.</summary>
    internal static class Pickers
    {
        public static ListPickerForm<CableModel> Cable(List<CableModel> cables, string okText) =>
            new ListPickerForm<CableModel>(cables, "Selecionar cabo", "FIBRA_LANCAR_CABO",
                $"{cables.Count} cabo(s) cadastrados em Dados\\{CableProvider.FileName}",
                "Buscar por nome, tipo ou peso...", "Nenhum cabo encontrado", okText,
                c => (c.FullName, c.ShortName, $"{c.WeightKgKm.ToString("0.##", CultureInfo.CurrentCulture)} kg/km"),
                (c, q) => SearchBox.Matches(c.FullName, q) || SearchBox.Matches(c.ShortName, q) ||
                          SearchBox.Matches(c.WeightKgKm.ToString(CultureInfo.CurrentCulture), q),
                itemHeight: 44);

        /// <param name="current">Modelo já selecionado ao abrir (o último usado).</param>
        public static ListPickerForm<PoleData> Pole(List<PoleData> models, PoleData? current)
        {
            var form = new ListPickerForm<PoleData>(models, "Inserir Postes", "FIBRA_INSERIR_POSTE",
                $"{models.Count} modelo(s) cadastrados em Dados\\{PoleModels.FileName}",
                "Buscar (ex.: 11/300, CC, DT)...", "Nenhum modelo encontrado", "Inserir",
                m => (m.Designation,
                      $"{m.TypeName}  ·  {m.HeightM.ToString("0.#", CultureInfo.CurrentCulture)} m  ·  " +
                      $"{m.EffortDaN.ToString("0", CultureInfo.CurrentCulture)} daN",
                      m.Type),
                (m, q) => SearchBox.Matches(m.Designation, q) || SearchBox.Matches(m.TypeName, q));

            PoleData? match = current == null ? null : models.FirstOrDefault(m => m.Designation == current.Designation);
            if (match != null) form.SelectItem(match);
            return form;
        }
    }
}
