using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FiberPlugin.Core;
using FiberPlugin.Models;

namespace FiberPlugin.UI
{
    public class MainMenuForm : Form
    {
        private readonly FlowLayoutPanel flow;
        private readonly SearchBox search;
        private readonly List<(Label Header, List<CommandCard> Cards)> sections = new List<(Label, List<CommandCard>)>();
        private bool _sizing;

        public string? SelectedCommandName { get; private set; }

        public MainMenuForm()
        {
            Theme.ApplyForm(this);
            this.Text = "Fiber Plugin";
            this.ClientSize = new Size(800, 600);
            this.KeyPreview = true;

            var header = new HeaderPanel
            {
                Glyph = Theme.Icons.Fiber,
                Title = "Fiber Plugin",
                Subtitle = "Ferramentas para projetos de redes FTTH no AutoCAD"
            };

            // Busca
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 10, 16, 8), BackColor = Theme.Background };
            search = new SearchBox("Buscar ferramenta...") { Dock = DockStyle.Fill };
            toolbar.Controls.Add(search);

            // Cartões
            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(12, 0, 12, 12),
                BackColor = Theme.Background
            };
            Theme.UseDarkScrollbars(flow);

            foreach (var (sectionName, tools) in ToolCatalog.Sections)
            {
                var label = new Label
                {
                    Text = sectionName.ToUpperInvariant(),
                    Font = Theme.Section,
                    ForeColor = Theme.Muted,
                    AutoSize = false,
                    Height = 26,
                    Width = 740,
                    TextAlign = ContentAlignment.BottomLeft,
                    Margin = new Padding(4, 8, 4, 2)
                };
                flow.Controls.Add(label);
                flow.SetFlowBreak(label, true);

                var cards = new List<CommandCard>();
                foreach (Tool tool in tools)
                {
                    var card = new CommandCard(tool.Glyph, tool.Title, tool.Description, tool.Command);
                    card.Click += (s, e) => Run(card.CommandName);
                    flow.Controls.Add(card);
                    cards.Add(card);
                }
                flow.SetFlowBreak(cards[cards.Count - 1], true);
                sections.Add((label, cards));
            }

            // Rodapé
            var footer = new FooterPanel { Hint = LibraryStatus() };
            var btnClose = new ThemedButton("Fechar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(btnClose);
            this.CancelButton = btnClose;

            // Ordem de encaixe: o último adicionado encaixa primeiro
            this.Controls.Add(flow);
            this.Controls.Add(toolbar);
            this.Controls.Add(header);
            this.Controls.Add(footer);

            search.Input.TextChanged += (s, e) => ApplyFilter();
            search.Input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommandCard? first = VisibleCards().FirstOrDefault();
                    if (first != null) Run(first.CommandName);
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    VisibleCards().FirstOrDefault()?.Focus();
                    e.SuppressKeyPress = true;
                }
            };

            flow.ClientSizeChanged += (s, e) => FitCards();
            this.Shown += (s, e) =>
            {
                FitCards();
                search.Input.Focus();
            };
        }

        private void Run(string commandName)
        {
            SelectedCommandName = commandName;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private IEnumerable<CommandCard> VisibleCards()
        {
            return sections.SelectMany(s => s.Cards).Where(c => c.Visible);
        }

        private void ApplyFilter()
        {
            string query = search.Query;
            bool Matches(string text) => SearchBox.Matches(text, query);

            flow.SuspendLayout();
            foreach (var (header, cards) in sections)
            {
                bool sectionMatches = Matches(header.Text);
                foreach (CommandCard card in cards)
                {
                    card.Visible = sectionMatches || Matches(card.Title) || Matches(card.Description) || Matches(card.CommandName);
                }
                header.Visible = cards.Any(c => c.Visible);

                // A quebra de linha precisa estar no último cartão visível da seção
                CommandCard? lastVisible = cards.LastOrDefault(c => c.Visible);
                foreach (CommandCard card in cards) flow.SetFlowBreak(card, card == lastVisible);
            }
            flow.ResumeLayout();
        }

        /// <summary>Duas colunas de cartões ocupando toda a largura disponível.</summary>
        private void FitCards()
        {
            if (_sizing) return;
            _sizing = true;
            try
            {
                int available = flow.ClientSize.Width - flow.Padding.Horizontal;
                int margin = Theme.Scale(this, 4);
                int cardWidth = (available - 4 * margin) / 2 - 1;

                flow.SuspendLayout();
                foreach (var (header, cards) in sections)
                {
                    header.Width = available - 2 * margin;
                    foreach (CommandCard card in cards) card.Width = cardWidth;
                }
                flow.ResumeLayout();
            }
            finally
            {
                _sizing = false;
            }
        }

        /// <summary>Resumo das pastas Dados e Blocos para o rodapé.</summary>
        private static string LibraryStatus()
        {
            string blocksText = PluginPaths.BlocksDir == null
                ? "pasta Blocos não encontrada"
                : $"{CountBlocks()} bloco(s) em {BlockRepository.LibraryFileName}";

            return $"{CountCables()} cabo(s) cadastrados  ·  {blocksText}  ·  Esc fecha";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string CountBlocks()
        {
            try
            {
                return BlockRepository.List().Count.ToString();
            }
            catch
            {
                return "?";
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string CountCables()
        {
            try
            {
                return CableProvider.GetCables().Count.ToString();
            }
            catch
            {
                return "?";
            }
        }
    }
}
