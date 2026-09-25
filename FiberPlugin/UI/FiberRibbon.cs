using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                    // WSCURRENT: troca de espaço de trabalho; COLORTHEME: troca de tema (claro/escuro)
                    if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("COLORTHEME", StringComparison.OrdinalIgnoreCase)) CreateTab();
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

            Theme.Refresh(); // Ícones na cor de destaque do tema atual do AutoCAD

            var tab = new RibbonTab { Title = "Fibra", Id = TabId };
            ribbon.Tabs.Add(tab);

            foreach (var (section, tools) in ToolCatalog.Sections)
            {
                var items = new List<RibbonItem>();

                // O último painel ganha também o botão do menu principal
                if (section == ToolCatalog.Sections[ToolCatalog.Sections.Length - 1].Section)
                {
                    items.Add(Button("Menu\nPrincipal", Theme.Icons.Fiber, ToolCatalog.MenuCommand,
                        "Abre o menu com todas as ferramentas.", large: true));
                }

                // Botões grandes primeiro; os pequenos ficam empilhados (um por linha)
                foreach (Tool tool in tools.Where(t => t.LargeOnRibbon))
                {
                    items.Add(Button(tool.RibbonLabel, tool.Glyph, tool.Command, tool.Description, large: true));
                }

                bool firstSmall = true;
                foreach (Tool tool in tools.Where(t => !t.LargeOnRibbon))
                {
                    if (!firstSmall) items.Add(new RibbonRowBreak());
                    items.Add(Button(tool.RibbonLabel, tool.Glyph, tool.Command, tool.Description, large: false));
                    firstSmall = false;
                }

                tab.Panels.Add(Panel(section, items));
            }
        }

        private static RibbonPanel Panel(string title, IEnumerable<RibbonItem> items)
        {
            var source = new RibbonPanelSource { Title = title };
            foreach (RibbonItem item in items) source.Items.Add(item);
            return new RibbonPanel { Source = source };
        }

        private static RibbonButton Button(string text, string glyph, string command, string tooltip, bool large)
        {
            return new RibbonButton
            {
                Id = "FIBER_" + command,
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = large ? Wpf.Controls.Orientation.Vertical : Wpf.Controls.Orientation.Horizontal,
                LargeImage = GlyphImage(glyph, 32),
                Image = GlyphImage(glyph, 16),
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
            System.Drawing.Color c = Theme.IconColor;
            var brush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(c.R, c.G, c.B));
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
