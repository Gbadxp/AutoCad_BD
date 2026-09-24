using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FiberPlugin.UI
{
    /// <summary>Cabeçalho com ícone em degradê, título e subtítulo.</summary>
    internal class HeaderPanel : Panel
    {
        public string Glyph { get; set; } = Theme.Icons.Fiber;
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";

        public HeaderPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 78;
            BackColor = Theme.Header;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int S(int v) => Theme.Scale(this, v);

            // Ícone
            var icon = new Rectangle(S(22), (Height - S(42)) / 2, S(42), S(42));
            using (var path = Theme.RoundedRect(icon, S(11)))
            using (var brush = new LinearGradientBrush(icon, Theme.Accent, Theme.AccentDeep, 45f))
            {
                g.FillPath(brush, path);
            }
            Theme.DrawGlyph(g, Glyph, Title, icon, Color.White, 15f);

            // Textos
            int titleH = Theme.Title.Height;
            int subH = Theme.Small.Height;
            int top = (Height - titleH - subH - S(2)) / 2;
            int left = icon.Right + S(14);
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(g, Title, Theme.Title, new Rectangle(left, top, Width - left - S(20), titleH), Theme.Text, flags);
            TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(left, top + titleH + S(2), Width - left - S(20), subH), Theme.Muted, flags);

            // Linha de "fibra" no rodapé do cabeçalho, sumindo para a direita
            var line = new Rectangle(0, Height - S(2), Width, S(2));
            using (var brush = new LinearGradientBrush(line, Theme.Accent, Theme.Header, 0f))
            {
                g.FillRectangle(brush, line);
            }
        }
    }

    /// <summary>Rodapé com dica à esquerda e botões alinhados à direita.</summary>
    internal class FooterPanel : Panel
    {
        private readonly List<Button> _buttons = new List<Button>();
        private string _hint = "";

        public string Hint
        {
            get => _hint;
            set { _hint = value; Invalidate(); }
        }

        public FooterPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Bottom;
            Height = 64;
            BackColor = Theme.Header;
        }

        /// <summary>Botões adicionados primeiro ficam mais à direita.</summary>
        public void AddButton(Button button)
        {
            _buttons.Add(button);
            Controls.Add(button);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int x = Width - Theme.Scale(this, 20);
            foreach (Button b in _buttons)
            {
                x -= b.Width;
                b.Location = new Point(x, (Height - b.Height) / 2);
                x -= Theme.Scale(this, 10);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }

            int right = _buttons.Count > 0 ? _buttons[_buttons.Count - 1].Left - Theme.Scale(this, 12) : Width;
            var rect = new Rectangle(Theme.Scale(this, 22), 0, Math.Max(0, right - Theme.Scale(this, 22)), Height);
            TextRenderer.DrawText(e.Graphics, _hint, Theme.Small, rect, Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Botão arredondado. Primary = ação principal em destaque.</summary>
    internal class ThemedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public bool Primary { get; set; }

        public ThemedButton(string text, bool primary)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text;
            Primary = primary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.BodyBold;
            Cursor = Cursors.Hand;
            Size = new Size(Math.Max(112, TextRenderer.MeasureText(text, Theme.BodyBold).Width + 36), 38);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Header);

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Theme.Scale(this, 8);

            Color fill, text;
            if (!Enabled)
            {
                fill = Theme.Surface;
                text = Theme.Muted;
            }
            else if (Primary)
            {
                fill = _pressed ? Theme.Accent : _hover ? Theme.AccentHover : Theme.Accent;
                text = Theme.OnAccent;
            }
            else
            {
                fill = _hover ? Theme.SurfaceHover : Theme.Surface;
                text = Theme.Text;
            }

            Theme.FillRounded(g, fill, r, radius);
            if (!Primary || !Enabled) Theme.DrawRounded(g, Theme.Border, r, radius);
            if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, RectangleF.Inflate(r, -2, -2), radius - 2, 1.5f);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Caixa de busca arredondada com ícone de lupa.</summary>
    internal class SearchBox : Panel
    {
        private readonly Label _placeholder;

        public TextBox Input { get; }

        public SearchBox(string placeholder)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.IBeam;

            Input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Body
            };
            Input.GotFocus += (s, e) => Invalidate();
            Input.LostFocus += (s, e) => Invalidate();
            Controls.Add(Input);

            // Texto de exemplo que continua visível com o cursor no campo
            _placeholder = new Label
            {
                Text = placeholder,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface,
                Font = Theme.Body,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.IBeam
            };
            _placeholder.Click += (s, e) => Input.Focus();
            Input.TextChanged += (s, e) => _placeholder.Visible = Input.TextLength == 0;
            Controls.Add(_placeholder);
            _placeholder.BringToFront();
            Height = 38;

            Click += (s, e) => Input.Focus();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int left = Theme.Scale(this, 38);
            Input.SetBounds(left, (Height - Input.Height) / 2, Math.Max(10, Width - left - Theme.Scale(this, 12)), Input.Height);
            // Deixa a coluna do cursor livre para ele continuar piscando
            _placeholder?.SetBounds(Input.Left + Theme.Scale(this, 3), Input.Top, Math.Max(10, Input.Width - Theme.Scale(this, 3)), Input.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Background);

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Theme.Scale(this, 8);
            Theme.FillRounded(g, Theme.Surface, r, radius);
            Theme.DrawRounded(g, Input.Focused ? Theme.Accent : Theme.Border, r, radius, Input.Focused ? 1.5f : 1f);

            Theme.DrawGlyph(g, Theme.Icons.Search, "", new Rectangle(Theme.Scale(this, 10), 0, Theme.Scale(this, 20), Height), Theme.Muted, 10.5f);
        }
    }

    /// <summary>Lista com itens desenhados: título, linha secundária e etiqueta à direita.</summary>
    internal class ThemedListBox : ListBox
    {
        public Func<object, (string Primary, string? Secondary, string? Badge)>? Describe { get; set; }
        public int LogicalItemHeight { get; set; } = 48;

        public ThemedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            IntegralHeight = false;
            Theme.UseDarkScrollbars(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemHeight = Math.Min(255, Theme.Scale(this, LogicalItemHeight));
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, e.Bounds);
            }

            int S(int v) => Theme.Scale(this, v);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var card = Rectangle.Inflate(e.Bounds, -S(2), -S(2));

            if (selected)
            {
                Theme.FillRounded(g, Theme.AccentSoft, card, S(7));
                Theme.FillRounded(g, Theme.Accent, new RectangleF(card.X + S(1), card.Y + S(9), S(3), card.Height - S(18)), S(2));
            }

            object item = Items[e.Index];
            var (primary, secondary, badge) = Describe?.Invoke(item) ?? (item.ToString() ?? "", null, null);

            int left = card.X + S(16);
            int right = card.Right - S(12);
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            if (!string.IsNullOrEmpty(badge))
            {
                const TextFormatFlags badgeFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                Size size = TextRenderer.MeasureText(g, badge, Theme.Small, Size.Empty, badgeFlags);
                int w = size.Width + S(10);
                int h = S(22);
                var pill = new Rectangle(right - w, card.Y + (card.Height - h) / 2, w, h);
                Theme.FillRounded(g, selected ? Theme.Background : Theme.Surface, pill, h / 2f);
                TextRenderer.DrawText(g, badge, Theme.Small, pill, selected ? Theme.AccentHover : Theme.Muted,
                    badgeFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                right = pill.Left - S(10);
            }

            if (string.IsNullOrEmpty(secondary))
            {
                TextRenderer.DrawText(g, primary, Theme.BodyBold, new Rectangle(left, card.Y, right - left, card.Height),
                    selected ? Theme.Text : Theme.Text, flags | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int h1 = Theme.BodyBold.Height;
                int h2 = Theme.Small.Height;
                int top = card.Y + (card.Height - h1 - h2 - S(2)) / 2;
                TextRenderer.DrawText(g, primary, Theme.BodyBold, new Rectangle(left, top, right - left, h1), Theme.Text, flags);
                TextRenderer.DrawText(g, secondary, Theme.Small, new Rectangle(left, top + h1 + S(2), right - left, h2),
                    selected ? Theme.AccentHover : Theme.Muted, flags);
            }
        }
    }

    /// <summary>Cartão clicável do menu principal.</summary>
    internal class CommandCard : Control
    {
        private bool _hover;

        public string Glyph { get; }
        public string Title { get; }
        public string Description { get; }
        public string CommandName { get; }

        public CommandCard(string glyph, string title, string description, string commandName)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Glyph = glyph;
            Title = title;
            Description = description;
            CommandName = commandName;
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(340, 70);
            Margin = new Padding(6);
            AccessibleName = title;
            AccessibleDescription = description;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode is Keys.Right or Keys.Down or Keys.Left or Keys.Up)
            {
                bool forward = e.KeyCode is Keys.Right or Keys.Down;
                Parent?.SelectNextControl(this, forward, true, false, true);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Background);

            int S(int v) => Theme.Scale(this, v);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = S(10);

            Theme.FillRounded(g, _hover ? Theme.SurfaceHover : Theme.Surface, r, radius);
            Theme.DrawRounded(g, Focused ? Theme.Accent : _hover ? Theme.Muted : Theme.Border, r, radius, Focused ? 1.5f : 1f);

            // Ícone
            int iconSize = S(40);
            var icon = new Rectangle(S(14), (Height - iconSize) / 2, iconSize, iconSize);
            Theme.FillRounded(g, _hover || Focused ? Theme.Accent : Theme.AccentSoft, icon, S(9));
            Theme.DrawGlyph(g, Glyph, Title, icon, _hover || Focused ? Theme.OnAccent : Theme.AccentHover, 14f);

            // Textos
            int left = icon.Right + S(14);
            int width = Width - left - S(12);
            int h1 = Theme.BodyBold.Height;
            int h2 = Theme.Small.Height;
            int top = (Height - h1 - h2 - S(3)) / 2;
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(g, Title, Theme.BodyBold, new Rectangle(left, top, width, h1), Theme.Text, flags);
            TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(left, top + h1 + S(3), width, h2), Theme.Muted, flags);
        }
    }
}
