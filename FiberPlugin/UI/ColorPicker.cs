using System.Drawing.Drawing2D;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Cores do índice de cor do AutoCAD (ACI, 1 a 255) para desenhar as amostras nas janelas. 1 a 9 são as cores
    /// padrão; 10 a 249 variam a matiz de 15 em 15 graus (dezena) e o brilho/saturação (unidade); 250 a 255 são cinzas.
    /// </summary>
    internal static class AciColors
    {
        private static readonly Color[] Standard =
        {
            Color.Black,
            Color.FromArgb(255, 0, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0),
            Color.FromArgb(0, 255, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 0, 255),
            Color.FromArgb(255, 255, 255), Color.FromArgb(128, 128, 128), Color.FromArgb(192, 192, 192)
        };

        private static readonly int[] Grays = { 51, 80, 105, 130, 190, 255 };              // 250 a 255
        private static readonly double[] Brightness = { 1.0, 0.8, 0.6, 0.5, 0.3 };           // unidade 0-1, 2-3...

        private static readonly string[] Names =
            { "", "vermelho", "amarelo", "verde", "ciano", "azul", "magenta", "branco", "cinza", "cinza claro" };

        public static Color Rgb(short index)
        {
            if (index >= 1 && index <= 9) return Standard[index];
            if (index >= 250 && index <= 255)
            {
                int g = Grays[index - 250];
                return Color.FromArgb(g, g, g);
            }
            if (index < 10 || index > 249) return Color.Black;

            double hue = (index / 10 - 1) * 15.0;
            int variant = index % 10;
            double v = 255 * Brightness[variant / 2];
            double s = variant % 2 == 0 ? 1.0 : 0.5;

            int sector = (int)(hue / 60);
            double f = hue / 60 - sector;
            double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            (double r, double gr, double b) = sector switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q)
            };
            return Color.FromArgb((int)r, (int)gr, (int)b);
        }

        /// <summary>"3 · verde" para as cores padrão, "Cor 32" para as outras.</summary>
        public static string Label(short index) =>
            index >= 1 && index <= 9 ? $"{index} · {Names[index]}" : $"Cor {index}";

        /// <summary>Quadradinho da cor, com contorno para aparecer em qualquer fundo.</summary>
        public static void DrawSwatch(Graphics g, Rectangle r, short index, bool dimmed = false)
        {
            Color color = Rgb(index);
            if (dimmed) color = Color.FromArgb(150, color);
            using (var brush = new SolidBrush(color)) g.FillRectangle(brush, r);
            using (var pen = new Pen(Theme.BorderHover)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
    }

    /// <summary>Grade com as 255 cores do AutoCAD, como na aba "Index Color" da caixa de cores dele.</summary>
    internal class AciGrid : Control
    {
        private readonly List<(short Index, Rectangle Bounds)> _cells = new List<(short, Rectangle)>();
        private short _hover;
        private short _selected;

        public event EventHandler? SelectedChanged;
        public event EventHandler? HoverChanged;
        public event EventHandler? Confirmed;

        public AciGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Background;
            TabStop = true;
        }

        public short Hover => _hover;

        public short Selected
        {
            get => _selected;
            set
            {
                if (value == _selected) return;
                _selected = value;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 24 matizes x 10 variações em cima e, embaixo, as cores padrão (1 a 9) à esquerda e os cinzas à direita.
        /// O tamanho das células acompanha o tamanho da janela; a grade fica centralizada.
        /// </summary>
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            _cells.Clear();
            int spacing = Theme.Scale(this, 12);
            // Altura = 10 células + espaço + linha de baixo (células grandes, 22/16 da célula)
            int cell = Math.Max(6, (int)Math.Min(Width / 24.0, (Height - spacing) / (10 + 22 / 16.0)));
            int big = cell * 22 / 16;
            int gap = Math.Max(1, cell / 16);
            int left = (Width - 24 * cell) / 2;

            for (short index = 10; index <= 249; index++)
            {
                int column = index / 10 - 1, row = index % 10;
                _cells.Add((index, new Rectangle(left + column * cell, row * cell, cell - gap, cell - gap)));
            }

            int top = 10 * cell + spacing;
            int bigGap = Math.Max(2, big / 11);
            for (short index = 1; index <= 9; index++)
            {
                _cells.Add((index, new Rectangle(left + (index - 1) * big, top, big - bigGap, big - bigGap)));
            }
            int grays = left + 24 * cell - 6 * big;
            for (short index = 250; index <= 255; index++)
            {
                _cells.Add((index, new Rectangle(grays + (index - 250) * big, top, big - bigGap, big - bigGap)));
            }
        }

        private short HitTest(Point p) => _cells.FirstOrDefault(c => c.Bounds.Contains(p)).Index;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            short hit = HitTest(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Invalidate();
            HoverChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = 0;
            Invalidate();
            HoverChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            short hit = HitTest(e.Location);
            if (hit != 0) Selected = hit;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (HitTest(e.Location) != 0) Confirmed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            foreach (var (index, bounds) in _cells)
            {
                using (var brush = new SolidBrush(AciColors.Rgb(index))) g.FillRectangle(brush, bounds);
            }

            foreach (var (index, width) in new[] { (_hover, 1f), (_selected, 2f) })
            {
                Rectangle r = _cells.FirstOrDefault(c => c.Index == index).Bounds;
                if (index == 0 || r.IsEmpty) continue;
                r.Inflate(1, 1);
                using (var outer = new Pen(Theme.TextStrong, width)) g.DrawRectangle(outer, r);
                r.Inflate(-(int)Math.Ceiling(width), -(int)Math.Ceiling(width));
                using (var inner = new Pen(Theme.Background, 1f)) g.DrawRectangle(inner, r);
            }
        }
    }

    /// <summary>Escolha de uma cor do AutoCAD (1 a 255), opcionalmente com a opção de voltar para a cor padrão.</summary>
    internal class ColorPickerForm : Form
    {
        private readonly AciGrid _grid;
        private readonly Label _info;

        /// <summary>Cor escolhida; null = cor padrão (botão "Cor padrão").</summary>
        public short? Result { get; private set; }

        /// <param name="current">Cor atual (null = usando a padrão).</param>
        /// <param name="defaultColor">Cor padrão mostrada no botão; null = sem o botão.</param>
        public ColorPickerForm(string title, short? current, short? defaultColor)
        {
            Theme.ApplyForm(this);
            Text = "Fiber Plugin - " + title;
            // Grade de 24 x 16 = 384 de largura e 10 x 16 + 12 + 22 = 194 de altura, com margens, informação e rodapé
            ClientSize = new Size(420, 356);

            var header = new HeaderPanel { IconCommand = "FIBRA_CONFIGURACOES", Title = title, Subtitle = "Cores do AutoCAD (1 a 255)" };
            _grid = new AciGrid { Selected = current ?? defaultColor ?? 7, Dock = DockStyle.Fill };
            _info = new Label { AutoSize = false, Height = 30, Dock = DockStyle.Bottom, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(18, 14, 18, 2) };
            body.Controls.Add(_grid);
            body.Controls.Add(_info);

            var footer = new FooterPanel();
            var ok = new ThemedButton("OK", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(ok);
            footer.AddButton(cancel);
            if (defaultColor is short fallback)
            {
                var useDefault = new ThemedButton($"Cor padrão ({AciColors.Label(fallback)})", false);
                useDefault.Click += (s, e) => { Result = null; DialogResult = DialogResult.OK; Close(); };
                footer.AddButton(useDefault);
            }

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = cancel;

            ok.Click += (s, e) => Confirm();
            _grid.Confirmed += (s, e) => Confirm();
            _grid.SelectedChanged += (s, e) => ShowInfo();
            _grid.HoverChanged += (s, e) => ShowInfo();
            ShowInfo();
            Shown += (s, e) => _grid.Focus();
        }

        private void ShowInfo()
        {
            string selected = "Escolhida: " + AciColors.Label(_grid.Selected);
            _info.Text = _grid.Hover != 0 && _grid.Hover != _grid.Selected ? $"{selected}   ·   {AciColors.Label(_grid.Hover)}" : selected;
        }

        private void Confirm()
        {
            Result = _grid.Selected;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Abre a escolha. Retorna false se o usuário cancelar; <paramref name="color"/> null = cor padrão.</summary>
        public static bool Pick(IWin32Window? owner, string title, short? current, short? defaultColor, out short? color)
        {
            using (var form = new ColorPickerForm(title, current, defaultColor))
            {
                color = current;
                if (form.ShowDialog(owner) != DialogResult.OK) return false;
                color = form.Result;
                return true;
            }
        }
    }

    /// <summary>Campo de cor com título em cima (como o LabeledInput): amostra + nome; clique abre a paleta.</summary>
    internal class ColorField : Panel
    {
        private readonly Swatch _swatch;
        private short _value;

        public event EventHandler? ValueChanged;

        public ColorField(string title)
        {
            BackColor = Theme.Background;
            Height = 50;
            Margin = new Padding(4, 2, 4, 2);
            _swatch = new Swatch(this) { Dock = DockStyle.Top, Height = 28 };
            var label = new Label
            {
                Text = title.ToUpperInvariant(),
                Font = Theme.Section,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Height = 20,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true
            };
            Controls.Add(_swatch);
            Controls.Add(label);
            Title = title;
        }

        public string Title { get; }

        public short Value
        {
            get => _value;
            set
            {
                if (value == _value) return;
                _value = value;
                _swatch.Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private sealed class Swatch : Control
        {
            private readonly ColorField _owner;
            private bool _hover;

            public Swatch(ColorField owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                if (ColorPickerForm.Pick(FindForm(), _owner.Title, _owner.Value, null, out short? color) && color is short c) _owner.Value = c;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent?.BackColor ?? Theme.Background);
                var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                float radius = Theme.Scale(this, Theme.Radius);
                Theme.FillRounded(g, _hover ? Theme.SurfaceHover : Theme.Input, r, radius);
                Theme.DrawRounded(g, Focused ? Theme.Accent : _hover ? Theme.BorderHover : Theme.Border, r, radius);
                g.SmoothingMode = SmoothingMode.None;

                int size = Height - Theme.Scale(this, 12);
                var swatch = new Rectangle(Theme.Scale(this, 8), (Height - size) / 2, size * 2, size);
                AciColors.DrawSwatch(g, swatch, _owner.Value);
                var text = new Rectangle(swatch.Right + Theme.Scale(this, 8), 0, Width - swatch.Right - Theme.Scale(this, 16), Height);
                TextRenderer.DrawText(g, AciColors.Label(_owner.Value), Theme.Body, text, Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
