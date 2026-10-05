using System.Drawing.Drawing2D;
using System.Globalization;

namespace FiberPlugin.UI
{
    // Controles desenhados no estilo da interface do AutoCAD: cores do tema ativo, cantos quase retos,
    // campos que clareiam no foco com borda azul e seleção azul-acinzentada com contorno.

    /// <summary>Faixa de título: ícone, título e subtítulo, com separador fino embaixo.</summary>
    internal class HeaderPanel : Panel
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";

        /// <summary>Comando cujo ícone colorido aparece no cabeçalho.</summary>
        public string IconCommand { get; set; } = ToolCatalog.MenuCommand;

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
            Theme.DrawIconAndText(g, this, Theme.Scale(this, 16), IconCommand, Title, Theme.Title, Subtitle);

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

    /// <summary>
    /// Campo de texto igual ao do AutoCAD: clareia e ganha borda azul quando está em foco.
    /// Com lupa, é o campo de busca das janelas.
    /// </summary>
    internal class InputBox : Panel
    {
        private readonly bool _searchIcon;
        private readonly Label _placeholder;
        private bool _hover;

        public TextBox Input { get; }

        public InputBox(string placeholder, bool searchIcon = true)
        {
            _searchIcon = searchIcon;
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

        /// <summary>Faixa no topo da janela com este campo de busca ocupando a largura toda.</summary>
        public Panel InToolbar()
        {
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 10, 16, 8), BackColor = Theme.Background };
            Dock = DockStyle.Fill;
            toolbar.Controls.Add(this);
            return toolbar;
        }

        /// <summary>Busca sem diferenciar maiúsculas nem acentos ("esforco" encontra "Esforço").</summary>
        public static bool Matches(string text, string query)
        {
            return query.Length == 0 ||
                   CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        /// <summary>Texto digitado, sem espaços nas pontas.</summary>
        public string Query => Input.Text.Trim();

        /// <summary>Texto de exemplo mostrado com o campo vazio.</summary>
        public string Placeholder
        {
            get => _placeholder.Text;
            set => _placeholder.Text = value;
        }

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
            int left = Theme.Scale(this, _searchIcon ? 28 : 9);
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

            if (_searchIcon) Theme.DrawSearchIcon(g, new Rectangle(Theme.Scale(this, 6), 0, Theme.Scale(this, 18), Height), Theme.Muted);
        }
    }

    /// <summary>Opções lado a lado em que só uma fica marcada (a marcada aparece no azul do botão principal).</summary>
    internal class ChoiceBar : FlowLayoutPanel
    {
        private readonly List<ThemedButton> _buttons = new List<ThemedButton>();
        private int _selected = -1;

        public event EventHandler? SelectedChanged;

        public ChoiceBar(params string[] options)
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = false;
            Margin = new Padding(0, 3, 0, 3);
            Padding = Padding.Empty;
            BackColor = Theme.Background;
            foreach (string option in options)
            {
                var button = new ThemedButton(option, false) { Margin = new Padding(0, 0, 4, 0) };
                int index = _buttons.Count;
                button.Click += (s, e) => SelectedIndex = index;
                _buttons.Add(button);
                Controls.Add(button);
            }
            SelectedIndex = 0;
        }

        /// <summary>Opção marcada; -1 = nenhuma (usado quando as opções são atalhos para preencher um campo).</summary>
        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value == _selected || value < -1 || value >= _buttons.Count) return;
                _selected = value;
                for (int i = 0; i < _buttons.Count; i++)
                {
                    _buttons[i].Primary = i == value;
                    _buttons[i].Invalidate();
                }
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Título de seção das janelas: maiúsculas, em cinza, encostado na parte de baixo da linha.</summary>
    internal class SectionLabel : Label
    {
        public SectionLabel(string text)
        {
            Text = text.ToUpperInvariant();
            Font = Theme.Section;
            ForeColor = Theme.Muted;
            TextAlign = ContentAlignment.BottomLeft;
            Margin = Padding.Empty;
        }
    }

    internal enum StatusKind
    {
        Hint,   // Orientação (cinza): falta preencher algo
        Ok,     // Resumo do que foi digitado
        Error   // Valor errado (vermelho)
    }

    /// <summary>Linha de status das janelas que conferem enquanto o usuário digita.</summary>
    internal class StatusLabel : Label
    {
        public StatusLabel()
        {
            AutoSize = false;
            TextAlign = ContentAlignment.MiddleLeft;
            ForeColor = Theme.Muted;
        }

        public void Set(string text, StatusKind kind)
        {
            Text = text;
            ForeColor = kind == StatusKind.Error ? Theme.Error : kind == StatusKind.Ok ? Theme.Text : Theme.Muted;
        }
    }

    /// <summary>Campo de texto com o título em cima (em maiúsculas, como nas seções das janelas).</summary>
    internal class LabeledInput : Panel
    {
        private readonly Label _title;

        public InputBox Box { get; }

        public LabeledInput(string title, string placeholder)
        {
            BackColor = Theme.Background;
            Height = 50;
            Margin = new Padding(4, 2, 4, 2);
            Box = new InputBox(placeholder, searchIcon: false) { Dock = DockStyle.Top };
            _title = new Label
            {
                Font = Theme.Section,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Height = 20,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true
            };
            Title = title;
            // O último adicionado encaixa primeiro: título em cima, campo embaixo
            Controls.Add(Box);
            Controls.Add(_title);
        }

        public string Title
        {
            get => _title.Text;
            set => _title.Text = value.ToUpperInvariant();
        }

        public Color TitleColor
        {
            get => _title.ForeColor;
            set => _title.ForeColor = value;
        }

        public string Value
        {
            get => Box.Query;
            set => Box.Input.Text = value;
        }
    }

    /// <summary>
    /// Lista de escolha com o título em cima (como o LabeledInput): campo com a opção escolhida e uma seta; o clique
    /// (ou Enter, Espaço, seta para baixo) abre as opções num menu com as cores do tema.
    /// </summary>
    internal class LabeledCombo : Panel
    {
        private readonly List<string> _items;
        private readonly DropButton _button;
        private string _value = "";

        public event EventHandler? ValueChanged;

        public LabeledCombo(string title, IEnumerable<string> items)
        {
            _items = items.ToList();
            BackColor = Theme.Background;
            Height = 50;
            Margin = new Padding(4, 2, 4, 2);
            _button = new DropButton(this) { Dock = DockStyle.Top, Height = 28 };
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
            Controls.Add(_button);
            Controls.Add(label);
            if (_items.Count > 0) _value = _items[0];
        }

        /// <summary>Opção escolhida; um texto fora da lista entra no fim dela.</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (value.Length > 0 && !_items.Contains(value)) _items.Add(value);
                string chosen = value.Length > 0 ? value : _items.FirstOrDefault() ?? "";
                if (chosen == _value) return;
                _value = chosen;
                _button.Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColors()),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Font = Theme.Body,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text
            };
            foreach (string item in _items)
            {
                var entry = new ToolStripMenuItem(item) { Checked = item == _value, ForeColor = Theme.Text };
                entry.Click += (s, e) => Value = item;
                menu.Items.Add(entry);
            }
            menu.MinimumSize = new Size(_button.Width, 0);
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(_button, new Point(0, _button.Height));
        }

        /// <summary>Campo da opção escolhida, desenhado como o InputBox, com a seta à direita.</summary>
        private sealed class DropButton : Control
        {
            private readonly LabeledCombo _owner;
            private bool _hover;

            public DropButton(LabeledCombo owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
                TabStop = true;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                Focus();
                _owner.ShowMenu();
            }

            protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down or Keys.Up || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down)
                {
                    _owner.ShowMenu();
                    e.Handled = true;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent?.BackColor ?? Theme.Background);
                var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                float radius = Theme.Scale(this, Theme.Radius);
                Theme.FillRounded(g, Focused || _hover ? Theme.InputFocused : Theme.Input, r, radius);
                Theme.DrawRounded(g, Focused ? Theme.Accent : _hover ? Theme.BorderHover : Theme.Border, r, radius);

                // Seta para baixo
                int cx = Width - Theme.Scale(this, 14), cy = Height / 2, s = Theme.Scale(this, 4);
                using (var brush = new SolidBrush(Theme.Muted))
                {
                    g.FillPolygon(brush, new[] { new Point(cx - s, cy - s / 2), new Point(cx + s, cy - s / 2), new Point(cx, cy + s / 2 + 1) });
                }

                var text = new Rectangle(Theme.Scale(this, 9), 0, Width - Theme.Scale(this, 34), Height);
                TextRenderer.DrawText(g, _owner.Value, Theme.Body, text, Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        /// <summary>Cores do menu de opções, iguais às das listas do tema.</summary>
        private sealed class ThemeMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Theme.SelectionBorder;
            public override Color MenuItemSelected => Theme.SurfaceHover;
            public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
            public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color CheckBackground => Theme.Selection;
            public override Color CheckSelectedBackground => Theme.Selection;
            public override Color CheckPressedBackground => Theme.Selection;
            public override Color SeparatorDark => Theme.Separator;
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

        public string Title { get; }
        public string Description { get; }
        public string CommandName { get; }

        public CommandCard(string title, string description, string commandName)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
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

            Theme.DrawIconAndText(g, this, S(12), CommandName, Title, Theme.BodyBold, Description);
        }
    }
}
