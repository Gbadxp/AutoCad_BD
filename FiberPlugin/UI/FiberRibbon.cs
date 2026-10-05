using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Wpf = System.Windows;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Aba "Fibra" na faixa de opções do AutoCAD, com os mesmos grupos do menu principal.
    /// </summary>
    internal static class FiberRibbon
    {
        private const string TabId = "FIBER_PLUGIN_TAB";
        private const int RowsPerColumn = 3;

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

                // Botões grandes primeiro
                foreach (Tool tool in tools.Where(t => t.LargeOnRibbon))
                {
                    items.Add(Button(tool, large: true));
                }

                // Botões pequenos em colunas de 3 linhas, como o painel Modificar do AutoCAD
                List<Tool> smalls = tools.Where(t => !t.LargeOnRibbon).ToList();
                for (int start = 0; start < smalls.Count; start += RowsPerColumn)
                {
                    var column = new RibbonRowPanel();
                    foreach (Tool tool in smalls.Skip(start).Take(RowsPerColumn))
                    {
                        if (column.Items.Count > 0) column.Items.Add(new RibbonRowBreak());
                        column.Items.Add(Button(tool, large: false));
                    }
                    items.Add(column);
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

        private static RibbonButton Button(Tool tool, bool large)
        {
            return new RibbonButton
            {
                Id = "FIBER_" + tool.Command,
                Text = tool.RibbonLabel,
                ShowText = true,
                ShowImage = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = large ? Wpf.Controls.Orientation.Vertical : Wpf.Controls.Orientation.Horizontal,
                LargeImage = RibbonIcons.Get(tool.Command, 32),
                Image = RibbonIcons.Get(tool.Command, 16),
                ToolTip = ToolTipFor(tool),
                CommandParameter = tool.Command,
                CommandHandler = RibbonCommandHandler.Instance
            };
        }

        /// <summary>Descrição do botão e, se houver, o atalho ativo do comando.</summary>
        private static string ToolTipFor(Tool tool)
        {
            string alias = Commands.ShortcutRegistry.AliasFor(tool.Command);
            return alias.Length > 0 ? $"{tool.Description}\nAtalho: {alias}" : tool.Description;
        }

        /// <summary>Atalhos mudaram (janela Configurações): atualiza a dica de cada botão da aba Fibra.</summary>
        public static void RefreshShortcuts()
        {
            RibbonTab? tab = ComponentManager.Ribbon?.FindTab(TabId);
            if (tab == null) return;
            Dictionary<string, Tool> tools = ToolCatalog.Sections.SelectMany(s => s.Tools).ToDictionary(t => "FIBER_" + t.Command);
            foreach (RibbonPanel panel in tab.Panels)
            {
                foreach (RibbonButton button in Buttons(panel.Source.Items))
                {
                    if (button.Id != null && tools.TryGetValue(button.Id, out Tool? tool)) button.ToolTip = ToolTipFor(tool);
                }
            }
        }

        private static IEnumerable<RibbonButton> Buttons(IEnumerable<RibbonItem> items)
        {
            foreach (RibbonItem item in items)
            {
                if (item is RibbonButton button) yield return button;
                else if (item is RibbonRowPanel row)
                    foreach (RibbonButton inner in Buttons(row.Items)) yield return inner;
            }
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
