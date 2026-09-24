using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Wpf = System.Windows;
using WpfMedia = System.Windows.Media;
using WpfImaging = System.Windows.Media.Imaging;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Aba "Fibra" na faixa de opções do AutoCAD, com os mesmos grupos do menu principal.
    /// </summary>
    internal static class FiberRibbon
    {
        private const string TabId = "FIBER_PLUGIN_TAB";

        // Ciano um pouco mais escuro que o das janelas, legível no tema claro e no escuro do AutoCAD
        private static readonly WpfMedia.Color IconColor = WpfMedia.Color.FromRgb(0x16, 0x9F, 0xB3);

        private static bool _workspaceHooked;

        /// <summary>Cria a aba agora, ou assim que a faixa de opções terminar de carregar.</summary>
        public static void Install()
        {
            if (ComponentManager.Ribbon == null)
            {
                ComponentManager.ItemInitialized += OnItemInitialized;
                return;
            }

            CreateTab();

            // Trocar de espaço de trabalho recria a faixa de opções e some com abas personalizadas
            if (!_workspaceHooked)
            {
                _workspaceHooked = true;
                AcApp.SystemVariableChanged += (s, e) =>
                {
                    if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase)) CreateTab();
                };
            }
        }

        private static void OnItemInitialized(object? sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Install();
        }

        public static void CreateTab()
        {
            RibbonControl? ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            RibbonTab? existing = ribbon.FindTab(TabId);
            if (existing != null) ribbon.Tabs.Remove(existing);

            var tab = new RibbonTab { Title = "Fibra", Id = TabId };
            ribbon.Tabs.Add(tab);

            tab.Panels.Add(Panel("Cabos",
                Large("Lançar\nCabo", Theme.Icons.Edit, "FIBRA_LANCAR_CABO", "Desenha o cabo clicando ponto a ponto, com nome e metragem em cada vão."),
                Large("Roteamento\nAutomático", Theme.Icons.Route, "FIBRA_ROTEAMENTO_AUTO", "Gera a rota mais curta entre os blocos selecionados.")));

            tab.Panels.Add(Panel("Postes e Blocos",
                Large("Inserir\nPostes", Theme.Icons.Pin, "FIBRA_INSERIR_POSTE", "Insere postes com numeração sequencial automática."),
                Large("Inserir\nBlocos", Theme.Icons.Blocks, "FIBRA_INSERIR_BLOCO", "Insere CTO, CEO e outros blocos do desenho ou da biblioteca."),
                Small("Nomear Postes", Theme.Icons.Tag, "FIBRA_NOMEAR_POSTE", "Grava o tipo do poste, ex.: DT 11/200 (o número após a barra é o esforço nominal)."),
                new RibbonRowBreak(),
                Small("Numerar Pontos", Theme.Icons.List, "FIBRA_NUMERAR_PONTOS", "Marca P01, P02... com as coordenadas X/Y.")));

            tab.Panels.Add(Panel("Esforços",
                Large("Esforço\nno Poste", Theme.Icons.Bolt, "FIBRA_ESFORCO_TOTAL", "Esforço resultante de todos os cabos no poste clicado, comparado com o nominal."),
                Large("Esforço no\nPercurso", Theme.Icons.Path, "FIBRA_ESFORCO_PERCURSO", "Coloca a seta de esforço em todos os postes de um cabo."),
                Large("Relatório de\nEsforços", Theme.Icons.Document, "FIBRA_RELATORIO_ESFORCOS", "Exporta CSV com esforço, nominal, utilização e situação (OK/EXCEDIDO).")));

            tab.Panels.Add(Panel("Materiais e Rede",
                Large("Lista de\nMateriais", Theme.Icons.Export, "FIBRA_EXPORTAR_CSV", "Exporta blocos, metragem de cabos e coordenadas para CSV."),
                Large("Calcular\nBobinas", Theme.Icons.Calculator, "FIBRA_CALCULAR_BOBINAS", "Quantidade de bobinas por tipo de cabo, com margem de segurança."),
                Large("Budget\nÓptico", Theme.Icons.Signal, "FIBRA_BUDGET_OPTICO", "Perdas do enlace GPON comparadas com a classe B+/C+.")));

            tab.Panels.Add(Panel("Fiber Plugin",
                Large("Menu\nPrincipal", Theme.Icons.Fiber, "FIBRA", "Abre o menu com todas as ferramentas."),
                Small("Exportar Blocos", Theme.Icons.Library, "FIBRA_EXPORTAR_BLOCOS", "Salva os blocos do desenho aberto na biblioteca (pasta Blocos)."),
                new RibbonRowBreak(),
                Small("Pasta de Dados", Theme.Icons.Search, "FIBRA_ABRIR_PASTA", "Abre a pasta com a planilha de cabos e a biblioteca de blocos.")));
        }

        private static RibbonPanel Panel(string title, params RibbonItem[] items)
        {
            var source = new RibbonPanelSource { Title = title };
            foreach (RibbonItem item in items) source.Items.Add(item);
            return new RibbonPanel { Source = source };
        }

        private static RibbonButton Large(string text, string glyph, string command, string tooltip)
        {
            return new RibbonButton
            {
                Id = "FIBER_" + command,
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = Wpf.Controls.Orientation.Vertical,
                LargeImage = GlyphImage(glyph, 32),
                Image = GlyphImage(glyph, 16),
                ToolTip = tooltip,
                CommandParameter = command,
                CommandHandler = RibbonCommandHandler.Instance
            };
        }

        private static RibbonButton Small(string text, string glyph, string command, string tooltip)
        {
            return new RibbonButton
            {
                Id = "FIBER_" + command,
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = Wpf.Controls.Orientation.Horizontal,
                Image = GlyphImage(glyph, 16),
                LargeImage = GlyphImage(glyph, 32),
                ToolTip = tooltip,
                CommandParameter = command,
                CommandHandler = RibbonCommandHandler.Instance
            };
        }

        /// <summary>Desenha o ícone (Segoe Fluent Icons / MDL2 Assets) numa imagem, sem precisar de arquivos .png.</summary>
        private static WpfMedia.ImageSource GlyphImage(string glyph, int size)
        {
            string? family = Theme.IconFamilyName;
            string text = family != null ? glyph : "•";
            var typeface = new WpfMedia.Typeface(new WpfMedia.FontFamily(family ?? "Segoe UI"),
                Wpf.FontStyles.Normal, Wpf.FontWeights.Normal, Wpf.FontStretches.Normal);
            var brush = new WpfMedia.SolidColorBrush(IconColor);
            brush.Freeze();

            var formatted = new WpfMedia.FormattedText(text, CultureInfo.InvariantCulture, Wpf.FlowDirection.LeftToRight,
                typeface, size * 0.82, brush, 1.0);

            var visual = new WpfMedia.DrawingVisual();
            using (WpfMedia.DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawText(formatted, new Wpf.Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
            }

            var bitmap = new WpfImaging.RenderTargetBitmap(size, size, 96, 96, WpfMedia.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }

    /// <summary>Executa o comando do botão na linha de comando do AutoCAD.</summary>
    internal class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public static readonly RibbonCommandHandler Instance = new RibbonCommandHandler();

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            string? command = parameter switch
            {
                RibbonCommandItem item => item.CommandParameter as string,
                string s => s,
                _ => null
            };
            if (string.IsNullOrEmpty(command)) return;

            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            // ^C^C cancela qualquer comando em andamento antes de iniciar o novo
            doc?.SendStringToExecute("\u0003\u0003" + command + " ", true, false, true);
        }
    }
}
