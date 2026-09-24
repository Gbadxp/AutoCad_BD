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

        // Ferramentas do menu, agrupadas por seção (o "Esforço 1 Cabo" fica fora de propósito)
        private static readonly (string Section, (string Glyph, string Title, string Description, string Command)[] Items)[] Tools =
        {
            ("Cabos", new[]
            {
                (Theme.Icons.Edit, "Lançar Rota Manual", "Desenhe o cabo clicando nos postes", "FIBRA_LANCAR_CABO"),
                (Theme.Icons.Route, "Roteamento Automático", "Rota mais curta entre os blocos", "FIBRA_ROTEAMENTO_AUTO")
            }),
            ("Postes e blocos", new[]
            {
                (Theme.Icons.Pin, "Inserir Postes", "Numeração sequencial automática", "FIBRA_INSERIR_POSTE"),
                (Theme.Icons.Blocks, "Inserir Blocos", "CTO, CEO e blocos da biblioteca", "FIBRA_INSERIR_BLOCO"),
                (Theme.Icons.Tag, "Nomear Postes", "Tipo e esforço nominal, ex.: DT 11/200", "FIBRA_NOMEAR_POSTE"),
                (Theme.Icons.List, "Numerar Pontos", "P01, P02... com coordenadas X/Y", "FIBRA_NUMERAR_PONTOS")
            }),
            ("Esforços", new[]
            {
                (Theme.Icons.Bolt, "Esforço Total no Poste", "Soma todos os cabos do poste clicado", "FIBRA_ESFORCO_TOTAL"),
                (Theme.Icons.Path, "Esforço no Percurso", "Setas em todos os postes de um cabo", "FIBRA_ESFORCO_PERCURSO"),
                (Theme.Icons.Document, "Relatório de Esforços", "CSV com a situação OK / EXCEDIDO", "FIBRA_RELATORIO_ESFORCOS")
            }),
            ("Materiais e rede", new[]
            {
                (Theme.Icons.Export, "Lista de Materiais", "Blocos, metragem e coordenadas (.csv)", "FIBRA_EXPORTAR_CSV"),
                (Theme.Icons.Calculator, "Calcular Bobinas", "Quantidade de bobinas por tipo de cabo", "FIBRA_CALCULAR_BOBINAS"),
                (Theme.Icons.Signal, "Budget Óptico", "Perdas do enlace GPON (B+ / C+)", "FIBRA_BUDGET_OPTICO")
            }),
            ("Biblioteca", new[]
            {
                (Theme.Icons.Library, "Exportar Blocos", "Salva os blocos do desenho na pasta", "FIBRA_EXPORTAR_BLOCOS")
            })
        };

        public MainMenuForm()
        {
            Theme.ApplyForm(this);
            this.Text = "Fiber Plugin";
            this.ClientSize = new Size(860, 700);
            this.KeyPreview = true;

            var header = new HeaderPanel
            {
                Glyph = Theme.Icons.Fiber,
                Title = "Fiber Plugin",
                Subtitle = "Ferramentas para projetos de redes FTTH no AutoCAD"
            };

            // Busca
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(22, 16, 22, 10), BackColor = Theme.Background };
            search = new SearchBox("Buscar ferramenta...") { Dock = DockStyle.Fill };
            toolbar.Controls.Add(search);

            // Cartões
            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(16, 0, 16, 16),
                BackColor = Theme.Background
            };
            Theme.UseDarkScrollbars(flow);

            foreach (var (sectionName, items) in Tools)
            {
                var label = new Label
                {
                    Text = sectionName.ToUpperInvariant(),
                    Font = Theme.Section,
                    ForeColor = Theme.Muted,
                    AutoSize = false,
                    Height = 30,
                    Width = 780,
                    TextAlign = ContentAlignment.BottomLeft,
                    Margin = new Padding(8, 10, 6, 2)
                };
                flow.Controls.Add(label);
                flow.SetFlowBreak(label, true);

                var cards = new List<CommandCard>();
                foreach (var (glyph, title, description, command) in items)
                {
                    var card = new CommandCard(glyph, title, description, command);
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
            string query = search.Input.Text.Trim();
            CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

            bool Matches(string text) => query.Length == 0 || compare.IndexOf(text, query, options) >= 0;

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
                int margin = Theme.Scale(this, 6);
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
            int blocks = BlockRepository.List().Count;
            string blocksText = PluginPaths.BlocksDir == null
                ? "pasta Blocos não encontrada"
                : $"{blocks} bloco(s) na biblioteca";

            return $"{CountCables()} cabo(s) cadastrados  ·  {blocksText}  ·  Esc fecha";
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
