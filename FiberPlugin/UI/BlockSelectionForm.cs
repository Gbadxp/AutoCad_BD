using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>Escolha de um bloco do BLOCOS.dwg, com coluna de categorias quando há mais de uma.</summary>
    internal class BlockSelectionForm : ListPickerForm<BlockEntry>
    {
        private const string AllCategories = "Todas";

        private class CategoryItem
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
        }

        private readonly ThemedListBox? _categories;

        /// <param name="heading">Título (ex.: "Inserir CTO").</param>
        /// <param name="iconCommand">Comando cujo ícone aparece no cabeçalho.</param>
        public BlockSelectionForm(List<BlockEntry> blocks, string heading, string iconCommand)
            : base(blocks, heading, iconCommand,
                $"{blocks.Count} bloco(s) na biblioteca {BlockRepository.LibraryFileName}",
                "Buscar bloco...", "Nenhum bloco encontrado", "Inserir",
                b => (b.Name, null, b.Category),
                (b, q) => SearchBox.Matches(b.Name, q),
                itemHeight: 32,
                hint: "Blocos da biblioteca são importados sozinhos")
        {
            List<string> categories = BlockCategories.Order
                .Concat(blocks.Select(b => b.Category).OrderBy(c => c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(c => blocks.Any(b => b.Category.Equals(c, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (categories.Count <= 1) return;

            // Coluna de categorias à esquerda da lista
            ClientSize = new Size(680, 500);
            _categories = new ThemedListBox
            {
                Dock = DockStyle.Left,
                Width = 180,
                LogicalItemHeight = 30,
                Describe = o =>
                {
                    var c = (CategoryItem)o;
                    return (c.Name, null, c.Count.ToString(CultureInfo.CurrentCulture));
                }
            };
            _categories.Items.Add(new CategoryItem { Name = AllCategories, Count = blocks.Count });
            foreach (string category in categories)
            {
                int count = blocks.Count(b => b.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
                _categories.Items.Add(new CategoryItem { Name = category, Count = count });
            }

            Body.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 10, BackColor = Theme.Background });
            Body.Controls.Add(_categories);

            _categories.SelectedIndexChanged += (s, e) => ApplyFilter();
            _categories.SelectedIndex = 0;
        }

        protected override bool Include(BlockEntry block)
        {
            string category = (_categories?.SelectedItem as CategoryItem)?.Name ?? AllCategories;
            return category == AllCategories || block.Category.Equals(category, StringComparison.OrdinalIgnoreCase);
        }
    }
}
