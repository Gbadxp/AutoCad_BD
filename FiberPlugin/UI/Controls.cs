using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace FiberPlugin.UI
{
    // Controles desenhados no estilo da interface do AutoCAD: cores do tema ativo, cantos quase retos,
    // campos que clareiam no foco com borda azul e seleção azul-acinzentada com contorno.

    /// <summary>Faixa de título: ícone, título e subtítulo, com separador fino embaixo.</summary>
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
            Height = 58;
            BackColor = Theme.Header;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            int S(int v) => Theme.Scale(this, v);

            var icon = new Rectangle(S(16), (Height - S(28)) / 2, S(28), S(28));
            Theme.DrawGlyph(g, Glyph, Title, icon, Theme.IconColor, 16f);

            int titleH = Theme.Title.Height;
            int subH = Theme.Small.Height;
            int top = (Height - titleH - subH) / 2;
            int left = icon.Right + S(10);
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(g, Title, Theme.Title, new Rectangle(left, top, Width - left - S(16), titleH), Theme.TextStrong, flags);
            TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(left, top + titleH, Width - left - S(16), subH), Theme.Muted, flags);

            using (var pen = new Pen(Theme.Separator))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    /// <summary>Rodapé com dica à esquerda e botões alinhados à direita, como nas caixas de diálogo do AutoCAD.</summary>
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
            Height = 50;
            BackColor = Theme.Background;
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
            int x = Width - Theme.Scale(this, 16);
            foreach (Button b in _buttons)
            {
                x -= b.Width;
                b.Location = new Point(x, (Height - b.Height) / 2);
                x -= Theme.Scale(this, 8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using (var pen = new Pen(Theme.Separator))
            {
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }

            int right = _buttons.Count > 0 ? _buttons[_buttons.Count - 1].Left - Theme.Scale(this, 12) : Width;
            var rect = new Rectangle(Theme.Scale(this, 16), 0, Math.Max(0, right - Theme.Scale(this, 16)), Height);
            TextRenderer.DrawText(e.Graphics, _hint, Theme.Small, rect, Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Botão no estilo AutoCAD. Primary = ação principal, no azul Autodesk.</summary>
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
            Font = Theme.Body;
            Size = new Size(Math.Max(88, TextRenderer.MeasureText(text, Theme.Body).Width + 28), 28);
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
            g.Clear(Parent?.BackColor ?? Theme.Background);

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.Scale(this, Theme.Radius);

            Color fill, border, text;
            if (!Enabled)
            {
                fill = Theme.Surface;
                border = Theme.Border;
                text = Theme.Muted;
            }
            else if (Primary)
            {
                fill = _hover && !_pressed ? Theme.PrimaryHover : Theme.Primary;
                border = fill;
                text = Theme.OnPrimary;
            }
            else
            {
                fill = _pressed ? Theme.Pressed : _hover ? Theme.SurfaceHover : Theme.Surface;
                border = _hover ? Theme.BorderHover : Theme.Border;
                text = Theme.Text;
            }

            Theme.FillRounded(g, fill, r, radius);
            Theme.DrawRounded(g, border, r, radius);
            if (Focused && ShowFocusCues) Theme.DrawRounded(g, Primary ? Theme.OnPrimary : Theme.Accent, RectangleF.Inflate(r, -2, -2), radius);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Campo de busca igual ao do AutoCAD: clareia e ganha borda azul quando está em foco.</summary>
    internal class SearchBox : Panel
    {
        private readonly Label _placeholder;
        private bool _hover;

        public TextBox Input { get; }

        public SearchBox(string placeholder)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.IBeam;

            Input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.Body
            };
            Input.GotFocus += (s, e) => UpdateColors();
            Input.LostFocus += (s, e) => UpdateColors();
            Controls.Add(Input);

            // Texto de exemplo que continua visível com o cursor no campo
            _placeholder = new Label
            {
                Text = placeholder,
                ForeColor = Theme.Muted,
                BackColor = Theme.Input,
                Font = Theme.Body,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.IBeam
            };
            _placeholder.Click += (s, e) => Input.Focus();
            _placeholder.MouseEnter += (s, e) => SetHover(true);
            _placeholder.MouseLeave += (s, e) => SetHover(false);
            Input.MouseEnter += (s, e) => SetHover(true);
            Input.MouseLeave += (s, e) => SetHover(false);
            Input.TextChanged += (s, e) => _placeholder.Visible = Input.TextLength == 0;
            Controls.Add(_placeholder);
            _placeholder.BringToFront();
            Height = 28;

            Click += (s, e) => Input.Focus();
        }

        /// <summary>Busca sem diferenciar maiúsculas nem acentos ("esforco" encontra "Esforço").</summary>
        public static bool Matches(string text, string query)
        {
            return query.Length == 0 ||
                   CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        /// <summary>Texto digitado, sem espaços nas pontas.</summary>
        public string Query => Input.Text.Trim();

        /// <summary>As setas ↑/↓ na busca mudam a seleção da lista sem tirar o cursor do campo.</summary>
        public void DriveList(ListBox list)
        {
            Input.KeyDown += (s, e) =>
            {
                if (e.KeyCode is not (Keys.Down or Keys.Up) || list.Items.Count == 0) return;

                int next = list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, next));
                e.SuppressKeyPress = true;
            };
        }

        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { SetHover(false); base.OnMouseLeave(e); }

        private void SetHover(bool hover)
        {
            _hover = hover;
            UpdateColors();
        }

        private Color Fill => Input.Focused || _hover ? Theme.InputFocused : Theme.Input;

        private void UpdateColors()
        {
            Input.BackColor = Fill;
            _placeholder.BackColor = Fill;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int left = Theme.Scale(this, 28);
            Input.SetBounds(left, (Height - Input.Height) / 2, Math.Max(10, Width - left - Theme.Scale(this, 8)), Input.Height);
            // Deixa a coluna do cursor livre para ele continuar piscando
            _placeholder?.SetBounds(Input.Left + Theme.Scale(this, 3), Input.Top, Math.Max(10, Input.Width - Theme.Scale(this, 3)), Input.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Background);

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.Scale(this, Theme.Radius);
            Theme.FillRounded(g, Fill, r, radius);
            Theme.DrawRounded(g, Input.Focused ? Theme.Accent : _hover ? Theme.BorderHover : Theme.Border, r, radius);

            Theme.DrawGlyph(g, Theme.Icons.Search, "", new Rectangle(Theme.Scale(this, 6), 0, Theme.Scale(this, 18), Height), Theme.Muted, 9f);
        }
    }

    /// <summary>Lista com itens desenhados (título, linha secundária e etiqueta), no estilo das listas do AutoCAD.</summary>
    internal class ThemedListBox : ListBox
    {
        private int _hoverIndex = -1;

        public Func<object, (string Primary, string? Secondary, string? Badge)>? Describe { get; set; }
        public int LogicalItemHeight { get; set; } = 44;

        public ThemedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface;
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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromPoint(e.Location);
            if (index == _hoverIndex) return;
            InvalidateItem(_hoverIndex);
            _hoverIndex = index;
            InvalidateItem(_hoverIndex);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            InvalidateItem(_hoverIndex);
            _hoverIndex = -1;
        }

        private void InvalidateItem(int index)
        {
            if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;

            Graphics g = e.Graphics;
            int S(int v) => Theme.Scale(this, v);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool hover = e.Index == _hoverIndex;

            using (var bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, e.Bounds);
            }

            var row = new Rectangle(e.Bounds.X + S(2), e.Bounds.Y + 1, e.Bounds.Width - S(4), e.Bounds.Height - 2);
            if (selected)
            {
                using (var fill = new SolidBrush(Theme.Selection)) g.FillRectangle(fill, row);
                using (var pen = new Pen(Theme.SelectionBorder)) g.DrawRectangle(pen, row.X, row.Y, row.Width - 1, row.Height - 1);
            }
            else if (hover)
            {
                using (var fill = new SolidBrush(Theme.SurfaceHover)) g.FillRectangle(fill, row);
            }

            object item = Items[e.Index];
            var (primary, secondary, badge) = Describe?.Invoke(item) ?? (item.ToString() ?? "", null, null);

            int left = row.X + S(10);
            int right = row.Right - S(10);
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Color secondaryColor = selected ? Theme.Text : Theme.Muted;

            if (!string.IsNullOrEmpty(badge))
            {
                const TextFormatFlags badgeFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                Size size = TextRenderer.MeasureText(g, badge, Theme.Small, Size.Empty, badgeFlags);
                var badgeRect = new Rectangle(right - size.Width, row.Y, size.Width, row.Height);
                TextRenderer.DrawText(g, badge, Theme.Small, badgeRect, secondaryColor, badgeFlags | TextFormatFlags.VerticalCenter);
                right = badgeRect.Left - S(10);
            }

            if (string.IsNullOrEmpty(secondary))
            {
                TextRenderer.DrawText(g, primary, Theme.Body, new Rectangle(left, row.Y, right - left, row.Height),
                    Theme.Text, flags | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int h1 = Theme.BodyBold.Height;
                int h2 = Theme.Small.Height;
                int top = row.Y + (row.Height - h1 - h2) / 2;
                TextRenderer.DrawText(g, primary, Theme.BodyBold, new Rectangle(left, top, right - left, h1), Theme.TextStrong, flags);
                TextRenderer.DrawText(g, secondary, Theme.Small, new Rectangle(left, top + h1, right - left, h2), secondaryColor, flags);
            }
        }
    }

    /// <summary>Botão de ferramenta do menu principal, no estilo dos botões de painel do AutoCAD.</summary>
    internal class CommandCard : Control
    {
        private bool _hover;
        private bool _pressed;

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
            Size = new Size(340, 54);
            Margin = new Padding(4);
            AccessibleName = title;
            AccessibleDescription = description;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
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
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = S(Theme.Radius);

            Color fill = _pressed ? Theme.Pressed : _hover ? Theme.SurfaceHover : Theme.Surface;
            Color border = Focused ? Theme.Accent : _hover ? Theme.BorderHover : Theme.Border;
            Theme.FillRounded(g, fill, r, radius);
            Theme.DrawRounded(g, border, r, radius);

            // Ícone
            var icon = new Rectangle(S(12), (Height - S(28)) / 2, S(28), S(28));
            Theme.DrawGlyph(g, Glyph, Title, icon, Theme.IconColor, 16f);

            // Textos
            int left = icon.Right + S(10);
            int width = Width - left - S(10);
            int h1 = Theme.BodyBold.Height;
            int h2 = Theme.Small.Height;
            int top = (Height - h1 - h2) / 2;
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(g, Title, Theme.BodyBold, new Rectangle(left, top, width, h1), Theme.TextStrong, flags);
            TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(left, top + h1, width, h2), Theme.Muted, flags);
        }
    }
}
