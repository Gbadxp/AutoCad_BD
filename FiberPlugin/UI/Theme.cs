using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Cores de um tema. Os valores vêm dos arquivos de tema do próprio AutoCAD
    /// (AutoCAD 2022\Themes\DarkTheme.xbel e LightTheme.xbel), com o nome original de cada cor.
    /// </summary>
    internal sealed class Palette
    {
        public bool IsDark { get; set; }
        public Color Background { get; set; }     // Fundo das janelas (MBFrameBackground)
        public Color Header { get; set; }         // Faixa de título e rodapé (HeaderBorderTop)
        public Color Surface { get; set; }        // Listas, campos e cartões (MBViewBackground / WidgetContainer1)
        public Color SurfaceHover { get; set; }   // Passar o mouse (HoverContainer1)
        public Color Pressed { get; set; }        // Clique (ClickContainer1)
        public Color Border { get; set; }         // Borda normal (SearchBoxNormalBorder)
        public Color BorderHover { get; set; }    // Borda ao passar o mouse (WidgetOutline1)
        public Color Separator { get; set; }      // Linhas divisórias (MBViewSeparator)
        public Color Text { get; set; }           // Texto (FontNormal1)
        public Color TextStrong { get; set; }     // Títulos (FontBold1)
        public Color Muted { get; set; }          // Texto secundário (Triangle2 / FontColorDisabled)
        public Color Accent { get; set; }         // Foco (SearchBoxFocusedBorder)
        public Color Selection { get; set; }      // Item selecionado (MBViewItemSelectionBackground)
        public Color SelectionBorder { get; set; } // Contorno da seleção (MBViewItemSelectionOutline)
        public Color Primary { get; set; }        // Botão principal (ControlActive1, azul Autodesk)
        public Color PrimaryHover { get; set; }
        public Color OnPrimary { get; set; }
        public Color Icon { get; set; }           // Ícones
        public Color Input { get; set; }          // Campo de texto (SearchBoxNormalBackground)
        public Color InputFocused { get; set; }   // Campo de texto com foco (SearchBoxFocusedBackground)

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            Background = Hex("#3B4453"),
            Header = Hex("#2E3440"),
            Surface = Hex("#454F61"),
            SurfaceHover = Hex("#535D6F"),        // #D1DEEE a 10% sobre #454F61
            Pressed = Hex("#616B7C"),             // #D1DEEE a 20% sobre #454F61
            Border = Hex("#2E3440"),
            BorderHover = Hex("#8B97A8"),         // #D1DEEE a 50% sobre #454F61
            Separator = Hex("#222933"),
            Text = Hex("#D9D9D9"),
            TextStrong = Hex("#F5F5F5"),
            Muted = Hex("#8691A1"),
            Accent = Hex("#38ABDF"),
            Selection = Hex("#416A86"),
            SelectionBorder = Hex("#38ABDF"),
            Primary = Hex("#0696D7"),
            PrimaryHover = Hex("#1BA6E6"),
            OnPrimary = Hex("#FFFFFF"),
            Icon = Hex("#38ABDF"),
            Input = Hex("#3B4453"),
            InputFocused = Hex("#4E5A6E")
        };

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            Background = Hex("#E1E1E1"),
            Header = Hex("#D2D2D2"),
            Surface = Hex("#FEFEFE"),
            SurfaceHover = Hex("#D1EBFA"),
            Pressed = Hex("#C3DCF4"),
            Border = Hex("#ABABAB"),
            BorderHover = Hex("#656565"),
            Separator = Hex("#C8C8C8"),
            Text = Hex("#191919"),
            TextStrong = Hex("#262626"),
            Muted = Hex("#737373"),
            Accent = Hex("#0160BF"),
            Selection = Hex("#B3CBEC"),
            SelectionBorder = Hex("#5A82B4"),
            Primary = Hex("#0696D7"),
            PrimaryHover = Hex("#0584BE"),
            OnPrimary = Hex("#FFFFFF"),
            Icon = Hex("#0160BF"),
            Input = Hex("#FEFEFE"),
            InputFocused = Hex("#F2F2F2")
        };

        private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);
    }

    /// <summary>
    /// Paleta e fontes das janelas do plugin, iguais às da interface do AutoCAD.
    /// Segue o tema escolhido no AutoCAD (variável COLORTHEME: 0 = escuro, 1 = claro), relido a cada janela.
    /// </summary>
    internal static class Theme
    {
        public static Palette Current { get; private set; } = Palette.Dark;

        public static Color Background => Current.Background;
        public static Color Header => Current.Header;
        public static Color Surface => Current.Surface;
        public static Color SurfaceHover => Current.SurfaceHover;
        public static Color Pressed => Current.Pressed;
        public static Color Border => Current.Border;
        public static Color BorderHover => Current.BorderHover;
        public static Color Separator => Current.Separator;
        public static Color Text => Current.Text;
        public static Color TextStrong => Current.TextStrong;
        public static Color Muted => Current.Muted;
        public static Color Accent => Current.Accent;
        public static Color Selection => Current.Selection;
        public static Color SelectionBorder => Current.SelectionBorder;
        public static Color Primary => Current.Primary;
        public static Color PrimaryHover => Current.PrimaryHover;
        public static Color OnPrimary => Current.OnPrimary;
        public static Color IconColor => Current.Icon;
        public static Color Input => Current.Input;
        public static Color InputFocused => Current.InputFocused;

        // Mesma fonte e tamanhos das caixas de diálogo do AutoCAD
        public static readonly Font Body = new Font("Segoe UI", 9f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 12f);

        // Cantos: o AutoCAD usa cantos quase retos
        public const int Radius = 2;

        /// <summary>
        /// Relê o tema do AutoCAD (COLORTHEME). Fora do AutoCAD (testes das janelas), usa a variável de
        /// ambiente FIBER_PLUGIN_TEMA=claro, ou o tema escuro.
        /// </summary>
        public static void Refresh()
        {
            int theme;
            try
            {
                theme = ReadAutoCadTheme();
            }
            catch
            {
                // Fora do AutoCAD a DLL da API nem carrega: o erro acontece ao chamar o método
                theme = Environment.GetEnvironmentVariable("FIBER_PLUGIN_TEMA") == "claro" ? 1 : 0;
            }
            Current = theme == 1 ? Palette.Light : Palette.Dark;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int ReadAutoCadTheme()
        {
            return Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("COLORTHEME"));
        }

        // Ícones: Segoe Fluent Icons (Windows 11) ou Segoe MDL2 Assets (Windows 10)
        private static readonly string? IconFamily = FindIconFamily();

        /// <summary>Nome da fonte de ícones instalada (null se nenhuma).</summary>
        public static string? IconFamilyName => IconFamily;

        public static class Icons
        {
            public const string Fiber = "\uE839";
            public const string Edit = "\uE70F";
            public const string Route = "\uE81E";
            public const string Pin = "\uE707";
            public const string Blocks = "\uECA5";
            public const string Tag = "\uE8EC";
            public const string List = "\uE8FD";
            public const string Bolt = "\uE945";
            public const string Path = "\uE7AD";
            public const string Document = "\uE8A5";
            public const string Export = "\uEDE1";
            public const string Calculator = "\uE8EF";
            public const string Signal = "\uE8BE";
            public const string Library = "\uE8F1";
            public const string Search = "\uE721";
            public const string Folder = "\uE8B7";
            public const string Ruler = "\uECC6";
            public const string Sheets = "\uE80A";
            public const string Page = "\uE7C3";
        }

        public static void ApplyForm(Form form)
        {
            Refresh();
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = Body;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterScreen;
            if (Current.IsDark) form.HandleCreated += (s, e) => UseDarkTitleBar(form.Handle);
        }

        /// <summary>Barras de rolagem escuras no tema escuro (Windows 10 1809+).</summary>
        public static void UseDarkScrollbars(Control control)
        {
            if (!Current.IsDark) return;
            control.HandleCreated += (s, e) =>
            {
                try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { /* Windows antigo: mantém o padrão */ }
            };
        }

        public static int Scale(Control control, int logicalPixels)
        {
            return (int)Math.Round(logicalPixels * control.DeviceDpi / 96f);
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = RoundedRect(r, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRounded(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
        {
            using (var path = RoundedRect(r, radius))
            using (var pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        /// <summary>Desenha o ícone; sem a fonte de ícones, usa a inicial do texto alternativo.</summary>
        public static void DrawGlyph(Graphics g, string glyph, string fallback, Rectangle bounds, Color color, float size)
        {
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                          TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (IconFamily != null)
            {
                using (var font = new Font(IconFamily, size))
                {
                    TextRenderer.DrawText(g, glyph, font, bounds, color, flags);
                }
            }
            else
            {
                string letter = string.IsNullOrEmpty(fallback) ? "•" : fallback.Substring(0, 1).ToUpperInvariant();
                TextRenderer.DrawText(g, letter, BodyBold, bounds, color, flags);
            }
        }

        private static string? FindIconFamily()
        {
            try
            {
                using (var fonts = new InstalledFontCollection())
                {
                    foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                    {
                        if (fonts.Families.Any(f => f.Name == name)) return name;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void UseDarkTitleBar(IntPtr handle)
        {
            try
            {
                int on = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 11 / 10 20H1+); 19 nas versões anteriores
                if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
    }
}
