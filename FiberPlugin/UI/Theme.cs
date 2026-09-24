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
    /// Paleta e fontes das janelas do plugin. Tema escuro para combinar com a interface do AutoCAD.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0x1B, 0x1F, 0x24);
        public static readonly Color Header = Color.FromArgb(0x14, 0x17, 0x1B);
        public static readonly Color Surface = Color.FromArgb(0x24, 0x29, 0x30);
        public static readonly Color SurfaceHover = Color.FromArgb(0x2D, 0x33, 0x3C);
        public static readonly Color Border = Color.FromArgb(0x38, 0x3F, 0x49);
        public static readonly Color Text = Color.FromArgb(0xE8, 0xEB, 0xEF);
        public static readonly Color Muted = Color.FromArgb(0x95, 0x9F, 0xAB);
        public static readonly Color Accent = Color.FromArgb(0x1F, 0xB6, 0xCC);       // Ciano "fibra"
        public static readonly Color AccentHover = Color.FromArgb(0x4A, 0xCB, 0xDD);
        public static readonly Color AccentDeep = Color.FromArgb(0x25, 0x63, 0xEB);   // Fim do degradê do ícone
        public static readonly Color AccentSoft = Color.FromArgb(0x16, 0x3D, 0x45);   // Fundo de item selecionado
        public static readonly Color OnAccent = Color.FromArgb(0x07, 0x19, 0x1D);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 14f);

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
        }

        public static void ApplyForm(Form form)
        {
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
            form.HandleCreated += (s, e) => UseDarkTitleBar(form.Handle);
        }

        /// <summary>Barras de rolagem escuras (Windows 10 1809+).</summary>
        public static void UseDarkScrollbars(Control control)
        {
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
