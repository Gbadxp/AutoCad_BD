using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    public class BlockSelectionForm : Form
    {
        private const string AllCategories = "Todas";

        private class CategoryItem
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
        }

        private readonly List<BlockEntry> allBlocks;
        private readonly SearchBox search;
        private readonly ThemedListBox categoryList;
        private readonly ThemedListBox blockList;
        private readonly Label emptyLabel;
        private readonly ThemedButton btnOk;

        public string? SelectedBlock { get; private set; }

        /// <param name="blocks">Blocos da pasta Blocos e/ou do desenho. A categoria vem da subpasta.</param>
        public BlockSelectionForm(List<BlockEntry> blocks, string title = "Fiber Plugin - Inserir Bloco")
        {
            allBlocks = blocks;

            Theme.ApplyForm(this);
            this.Text = title;
            this.ClientSize = new Size(720, 560);

            int fromLibrary = blocks.Count(b => b.FilePath != null);
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            var header = new HeaderPanel
            {
                Glyph = Theme.Icons.Blocks,
                Title = dash >= 0 ? title.Substring(dash + 3) : title,
                Subtitle = $"{blocks.Count} bloco(s)  ·  {fromLibrary} da pasta Blocos  ·  {blocks.Count - fromLibrary} só no desenho"
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(22, 16, 22, 10), BackColor = Theme.Background };
            search = new SearchBox("Buscar bloco...") { Dock = DockStyle.Fill };
            toolbar.Controls.Add(search);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 2, 20, 12), BackColor = Theme.Background };

            // Coluna de categorias (padrão primeiro, depois as subpastas extras criadas pelo usuário)
            categoryList = new ThemedListBox
            {
                Dock = DockStyle.Left,
                Width = 200,
                LogicalItemHeight = 42,
                BackColor = Theme.Header,
                Describe = o =>
                {
                    var c = (CategoryItem)o;
                    return (c.Name, null, c.Count.ToString(CultureInfo.CurrentCulture));
                }
            };
            categoryList.Items.Add(new CategoryItem { Name = AllCategories, Count = blocks.Count });
            var categoryOrder = BlockRepository.StandardCategories
                .Concat(blocks.Select(b => b.Category).Distinct().OrderBy(c => c))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string category in categoryOrder)
            {
                int count = blocks.Count(b => b.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
                if (count > 0) categoryList.Items.Add(new CategoryItem { Name = category, Count = count });
            }

            var spacer = new Panel { Dock = DockStyle.Left, Width = 14, BackColor = Theme.Background };

            blockList = new ThemedListBox
            {
                Dock = DockStyle.Fill,
                LogicalItemHeight = 52,
                Describe = o =>
                {
                    var b = (BlockEntry)o;
                    string origin = b.FilePath != null ? $"Pasta Blocos \\ {b.Category}" : "Já está no desenho";
                    return (b.Name, origin, b.FilePath != null ? "Biblioteca" : "Desenho");
                }
            };
            emptyLabel = new Label
            {
                Text = "Nenhum bloco encontrado",
                ForeColor = Theme.Muted,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 60,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            right.Controls.Add(blockList);
            right.Controls.Add(emptyLabel);

            body.Controls.Add(right);
            body.Controls.Add(spacer);
            body.Controls.Add(categoryList);

            var footer = new FooterPanel { Hint = "Blocos da biblioteca são importados sozinhos" };
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
            blockList.DoubleClick += (s, e) => Accept();
            categoryList.SelectedIndexChanged += (s, e) => ApplyFilter();
            search.Input.TextChanged += (s, e) => ApplyFilter();
            search.Input.KeyDown += (s, e) =>
            {
                if (e.KeyCode is Keys.Down or Keys.Up && blockList.Items.Count > 0)
                {
                    int next = blockList.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                    blockList.SelectedIndex = Math.Max(0, Math.Min(blockList.Items.Count - 1, next));
                    e.SuppressKeyPress = true;
                }
            };

            categoryList.SelectedIndex = 0;
            this.Shown += (s, e) => search.Input.Focus();
        }

        private void ApplyFilter()
        {
            string category = (categoryList.SelectedItem as CategoryItem)?.Name ?? AllCategories;
            string query = search.Input.Text.Trim();
            CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

            var matches = allBlocks
                .Where(b => category == AllCategories || b.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .Where(b => query.Length == 0 || compare.IndexOf(b.Name, query, options) >= 0)
                .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase);

            blockList.BeginUpdate();
            blockList.Items.Clear();
            foreach (BlockEntry b in matches) blockList.Items.Add(b);
            blockList.EndUpdate();

            if (blockList.Items.Count > 0) blockList.SelectedIndex = 0;
            emptyLabel.Visible = blockList.Items.Count == 0;
            btnOk.Enabled = blockList.Items.Count > 0;
        }

        private void Accept()
        {
            if (blockList.SelectedItem is not BlockEntry entry) return;
            SelectedBlock = entry.Name;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
