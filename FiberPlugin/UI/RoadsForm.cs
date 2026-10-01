using System.Drawing.Drawing2D;
using System.Globalization;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Janela do Importar Ruas: área por retângulo (os quatro lados, dispostos como no mapa) ou centro e raio,
    /// em latitude/longitude (graus decimais) ou UTM (metros), zona UTM e como desenhar. Confere enquanto digita
    /// e mostra o tamanho da área; trocar Lat/Long ↔ UTM ou Retângulo ↔ Centro converte o que já foi preenchido.
    /// </summary>
    internal class RoadsForm : Form
    {
        private static readonly string[] Styles = { "Contorno", "Eixo", "Ambos" };

        private readonly UtmSettings? _projectZone;
        private readonly ChoiceBar _mode;
        private readonly ChoiceBar _kind;
        private readonly InputBox _zone;
        private readonly ChoiceBar _hemisphere;
        private readonly Label _zoneHint;
        private readonly LabeledInput _north, _south, _east, _west;
        private readonly LabeledInput _centerA, _centerB, _radius;
        private readonly Label _pasteHint;
        private readonly SidesPanel _rectPanel;
        private readonly TableLayoutPanel _centerPanel;
        private readonly ChoiceBar _style;
        private readonly ChoiceBar _paths;
        private readonly ChoiceBar _names;
        private readonly Label _status;
        private readonly ThemedButton _ok;
        private bool _loading;
        private bool _wasRectangle, _wasGeographic;

        /// <summary>
        /// Marca a área no desenho: true = dois cantos, false = só o centro. Devolve as coordenadas do desenho
        /// (UTM) ou null se o usuário cancelar. Sem ele, o botão "Marcar no desenho" não aparece.
        /// </summary>
        public Func<bool, (double X1, double Y1, double X2, double Y2)?>? PickInDrawing { get; set; }

        /// <summary>Área válida (null enquanto faltar algo ou houver erro).</summary>
        public RoadArea? Area { get; private set; }

        public RoadImportSettings Settings => Read();

        /// <param name="projectZone">Zona UTM gravada no desenho; com ela a zona não é editável aqui.</param>
        public RoadsForm(RoadImportSettings saved, UtmSettings? projectZone)
        {
            _projectZone = projectZone;
            _loading = true;

            Theme.ApplyForm(this);
            Text = "Fiber Plugin - Importar Ruas";
            ClientSize = new Size(640, 692);

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_IMPORTAR_RUAS",
                Title = "Importar Ruas",
                Subtitle = "Ruas do OpenStreetMap já cortadas e georreferenciadas em UTM"
            };

            // ---------- Área ----------
            _mode = new ChoiceBar("Retângulo", "Centro e raio");
            _kind = new ChoiceBar("Lat/Long (graus decimais)", "UTM (metros)");
            _zone = new InputBox("20", searchIcon: false) { Size = new Size(56, 28), Margin = new Padding(0, 3, 6, 3) };
            _hemisphere = new ChoiceBar("Sul", "Norte");
            _zoneHint = new Label { AutoSize = false, Size = new Size(250, 20), AutoEllipsis = true, ForeColor = Theme.Muted, Margin = new Padding(6, 9, 0, 0) };
            var zoneRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
            zoneRow.Controls.AddRange(new Control[] { _zone, _hemisphere, _zoneHint });

            var options = OptionGrid(("Forma", _mode), ("Coordenadas", _kind), ("Zona UTM", zoneRow));

            // Retângulo: cada campo junto do lado que ele define, como no mapa
            _north = new LabeledInput("", "");
            _south = new LabeledInput("", "");
            _west = new LabeledInput("", "");
            _east = new LabeledInput("", "");
            _rectPanel = new SidesPanel(_north, _south, _east, _west) { Dock = DockStyle.Fill };

            // Centro e raio
            _centerA = new LabeledInput("", "") { Dock = DockStyle.Fill };
            _centerB = new LabeledInput("", "") { Dock = DockStyle.Fill };
            _radius = new LabeledInput("Raio (m)", "ex.: 1000") { Dock = DockStyle.Fill };
            _pasteHint = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Padding = new Padding(4, 2, 4, 0) };
            _centerPanel = Grid(3, new[] { 58f, 40f, 140f });
            _centerPanel.Controls.Add(_centerA, 0, 0);
            _centerPanel.Controls.Add(_centerB, 1, 0);
            _centerPanel.Controls.Add(_radius, 2, 0);
            _centerPanel.Controls.Add(_pasteHint, 0, 1);
            _centerPanel.SetColumnSpan(_pasteHint, 3);
            _centerPanel.Controls.Add(new CenterDiagram { Dock = DockStyle.Fill }, 1, 2);

            var areaPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0), BackColor = Theme.Background };
            areaPanel.Controls.Add(_rectPanel);
            areaPanel.Controls.Add(_centerPanel);

            var pick = new ThemedButton("Marcar no desenho", false) { Margin = new Padding(4, 2, 0, 2) };
            _status = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            var statusRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Background };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusRow.Controls.Add(pick, 0, 0);
            statusRow.Controls.Add(_status, 1, 0);

            // ---------- Desenho ----------
            _style = new ChoiceBar("Contorno (meio-fio)", "Eixo", "Contorno e eixo");
            _paths = new ChoiceBar("Só vias de veículos", "Também calçadas, ciclovias e trilhas");
            _names = new ChoiceBar("Sem nomes", "Com os nomes das ruas");
            var drawing = OptionGrid(("Ruas como", _style), ("Vias", _paths), ("Nomes", _names));

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 4, 18, 6), BackColor = Theme.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (control, height) in new (Control, float)[]
            {
                (Section("Área"), 26), (options, 108), (areaPanel, 248), (statusRow, 44), (Section("Desenho"), 30), (drawing, 108)
            })
            {
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                control.Dock = DockStyle.Fill;
                body.Controls.Add(control, 0, body.RowCount++);
            }
            // Sobra de altura numa linha vazia no fim (senão a última linha estica e desalinha os rótulos)
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowCount++;

            var footer = new FooterPanel { Hint = "Dados do OpenStreetMap (ODbL) · precisa de internet" };
            _ok = new ThemedButton("Importar", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(_ok);
            footer.AddButton(cancel);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            // ---------- Valores iniciais ----------
            _mode.SelectedIndex = saved.Rectangle ? 0 : 1;
            _kind.SelectedIndex = saved.Geographic ? 0 : 1;
            _north.Value = saved.North;
            _south.Value = saved.South;
            _east.Value = saved.East;
            _west.Value = saved.West;
            _centerA.Value = saved.CenterA;
            _centerB.Value = saved.CenterB;
            _radius.Value = saved.Radius;
            _style.SelectedIndex = Math.Max(0, Array.IndexOf(Styles, saved.Style));
            _paths.SelectedIndex = saved.IncludePaths ? 1 : 0;
            _names.SelectedIndex = saved.IncludeNames ? 1 : 0;
            if (projectZone != null)
            {
                // A zona do projeto vale para o desenho todo: troca só pelo botão Zona UTM
                _zone.Visible = false;
                _hemisphere.Visible = false;
                _zoneHint.Margin = new Padding(0, 9, 0, 0);
                _zoneHint.Width = 480;
                _zoneHint.ForeColor = Theme.Text;
                _zoneHint.Text = $"{projectZone.Zone} {(projectZone.South ? "Sul" : "Norte")}  ·  zona do projeto (botão Zona UTM para trocar)";
            }
            else
            {
                _zone.Input.Text = saved.Zone;
                _hemisphere.SelectedIndex = saved.SouthHemisphere ? 0 : 1;
            }
            _wasRectangle = saved.Rectangle;
            _wasGeographic = saved.Geographic;
            ApplyKindAndMode();
            _loading = false;

            // ---------- Eventos ----------
            _mode.SelectedChanged += (s, e) => Switch();
            _kind.SelectedChanged += (s, e) => Switch();
            _hemisphere.SelectedChanged += (s, e) => UpdateStatus();
            foreach (LabeledInput field in new[] { _north, _south, _east, _west, _centerA, _centerB, _radius })
                field.Box.Input.TextChanged += (s, e) => UpdateStatus();
            _zone.Input.TextChanged += (s, e) => UpdateStatus();

            // "-3.1019, -60.0250" colado no primeiro campo do centro vai para os dois campos
            _centerA.Box.Input.TextChanged += (s, e) =>
            {
                if (Coordinates.SplitPair(_centerA.Box.Input.Text) is not { } pair) return;
                _centerA.Value = pair.A;
                _centerB.Value = pair.B;
                _radius.Box.Input.Focus();
            };

            Load += (s, e) => ApplyKindAndMode();
            pick.Click += (s, e) => Pick();
            _ok.Click += (s, e) =>
            {
                if (Area == null) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (s, e) =>
            {
                pick.Visible = PickInDrawing != null;
                (_mode.SelectedIndex == 0 ? _north : _centerA).Box.Input.Focus();
            };

            UpdateStatus();
        }

        private RoadImportSettings Read() => new RoadImportSettings
        {
            Rectangle = _mode.SelectedIndex == 0,
            Geographic = _kind.SelectedIndex == 0,
            North = _north.Value,
            South = _south.Value,
            East = _east.Value,
            West = _west.Value,
            CenterA = _centerA.Value,
            CenterB = _centerB.Value,
            Radius = _radius.Value,
            Zone = _projectZone != null ? _projectZone.Zone.ToString(CultureInfo.InvariantCulture) : _zone.Query,
            SouthHemisphere = _projectZone?.South ?? _hemisphere.SelectedIndex == 0,
            Style = Styles[_style.SelectedIndex],
            IncludePaths = _paths.SelectedIndex == 1,
            IncludeNames = _names.SelectedIndex == 1
        };

        /// <summary>Trocou a forma ou o tipo de coordenada: converte a área já preenchida para o novo jeito.</summary>
        private void Switch()
        {
            RoadImportSettings before = Read();
            before.Rectangle = _wasRectangle;
            before.Geographic = _wasGeographic;
            RoadArea? area = before.Resolve(out _, out _);

            ApplyKindAndMode();
            if (area != null) Write(area);
            _wasRectangle = _mode.SelectedIndex == 0;
            _wasGeographic = _kind.SelectedIndex == 0;
            UpdateStatus();
        }

        /// <summary>Títulos e exemplos dos campos conforme Retângulo/Centro e Lat/Long/UTM.</summary>
        private void ApplyKindAndMode()
        {
            bool geo = _kind.SelectedIndex == 0;
            // Antes de a janela carregar os dois painéis ficam visíveis: o Windows Forms não ajusta a escala (DPI)
            // de um painel que começa escondido, e ele apareceria maior que o resto ao trocar para ele
            if (IsHandleCreated)
            {
                _rectPanel.Visible = _mode.SelectedIndex == 0;
                _centerPanel.Visible = _mode.SelectedIndex == 1;
            }

            _rectPanel.Geographic = geo;
            Set(_north, geo ? "Norte · latitude" : "Norte · N (m)", geo ? "ex.: -3.0950" : "ex.: 9657100");
            Set(_south, geo ? "Sul · latitude" : "Sul · N (m)", geo ? "ex.: -3.1170" : "ex.: 9656200");
            Set(_west, geo ? "Oeste · longitude" : "Oeste · E (m)", geo ? "ex.: -60.0300" : "ex.: 830300");
            Set(_east, geo ? "Leste · longitude" : "Leste · E (m)", geo ? "ex.: -60.0000" : "ex.: 831100");
            Set(_centerA, geo ? "Latitude do centro" : "E do centro (m)", geo ? "ex.: -3.1019" : "ex.: 830710");
            Set(_centerB, geo ? "Longitude do centro" : "N do centro (m)", geo ? "ex.: -60.0250" : "ex.: 9656678");
            _pasteHint.Text = geo
                ? "Dica: cole \"-3.1019, -60.0250\" do Google Maps na latitude: os dois campos se preenchem."
                : "Dica: cole \"830710 9656678\" no E: os dois campos se preenchem.";
            if (_projectZone == null)
                _zoneHint.Text = geo ? "vazia = calculada pela longitude" : "obrigatória em UTM";
        }

        private static void Set(LabeledInput field, string title, string placeholder)
        {
            field.Title = title;
            field.Box.Placeholder = placeholder;
        }

        /// <summary>Preenche os campos (na forma e no tipo de coordenada atuais) com a área em UTM.</summary>
        private void Write(RoadArea area)
        {
            _loading = true;
            bool geo = _kind.SelectedIndex == 0;
            UtmSettings z = area.Utm;
            if (_mode.SelectedIndex == 0)
            {
                if (geo)
                {
                    var corners = new[] { (area.MinX, area.MinY), (area.MinX, area.MaxY), (area.MaxX, area.MinY), (area.MaxX, area.MaxY) }
                        .Select(c => UtmZone.ToGeographic(c.Item1, c.Item2, z.Zone, z.South)).ToList();
                    _north.Value = Coordinates.Degrees(corners.Max(c => c.Lat));
                    _south.Value = Coordinates.Degrees(corners.Min(c => c.Lat));
                    _east.Value = Coordinates.Degrees(corners.Max(c => c.Lon));
                    _west.Value = Coordinates.Degrees(corners.Min(c => c.Lon));
                }
                else
                {
                    _north.Value = Coordinates.Meters(area.MaxY);
                    _south.Value = Coordinates.Meters(area.MinY);
                    _east.Value = Coordinates.Meters(area.MaxX);
                    _west.Value = Coordinates.Meters(area.MinX);
                }
            }
            else
            {
                double x = (area.MinX + area.MaxX) / 2, y = (area.MinY + area.MaxY) / 2;
                if (geo)
                {
                    var (lat, lon) = UtmZone.ToGeographic(x, y, z.Zone, z.South);
                    _centerA.Value = Coordinates.Degrees(lat);
                    _centerB.Value = Coordinates.Degrees(lon);
                }
                else
                {
                    _centerA.Value = Coordinates.Meters(x);
                    _centerB.Value = Coordinates.Meters(y);
                }
                _radius.Value = Math.Round(Math.Max(area.Width, area.Height) / 2).ToString(CultureInfo.InvariantCulture);
            }

            // Zona que tinha sido calculada pela longitude: UTM precisa dela escrita
            if (_projectZone == null && !geo && _zone.Query.Length == 0)
            {
                _zone.Input.Text = z.Zone.ToString(CultureInfo.InvariantCulture);
                _hemisphere.SelectedIndex = z.South ? 0 : 1;
            }
            _loading = false;
        }

        /// <summary>Esconde a janela, deixa marcar no desenho e preenche os campos.</summary>
        private void Pick()
        {
            bool rectangle = _mode.SelectedIndex == 0;
            if (PickInDrawing?.Invoke(rectangle) is not { } p) return;

            UtmSettings? zone = _projectZone;
            if (zone == null && int.TryParse(_zone.Query, NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) && z >= 1 && z <= 60)
                zone = new UtmSettings { Zone = z, South = _hemisphere.SelectedIndex == 0 };
            if (zone == null)
            {
                ShowStatus("Informe a zona UTM do desenho para usar os pontos marcados.", error: true);
                return;
            }

            var area = new RoadArea
            {
                MinX = Math.Min(p.X1, p.X2), MinY = Math.Min(p.Y1, p.Y2),
                MaxX = Math.Max(p.X1, p.X2), MaxY = Math.Max(p.Y1, p.Y2),
                Utm = zone
            };
            if (!rectangle)
            {
                double radius = Coordinates.ParseMeters(_radius.Value) is double r && r > 0 ? r : 1000;
                area.MinX -= radius; area.MaxX += radius;
                area.MinY -= radius; area.MaxY += radius;
            }
            Write(area);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_loading) return;
            Area = Read().Resolve(out string message, out bool incomplete);
            _ok.Enabled = Area != null;

            if (Area == null)
            {
                ShowStatus(message, error: !incomplete);
                return;
            }
            string zone = $"UTM {Area.Utm.Zone} {(Area.Utm.South ? "Sul" : "Norte")}" + (Area.ZoneFromCoordinates ? " (calculada)" : "");
            ShowStatus($"Área de {Area.Width:N0} × {Area.Height:N0} m  ·  {zone}" + (message.Length > 0 ? "  ·  " + message : ""),
                error: false, ok: message.Length == 0);
        }

        private void ShowStatus(string text, bool error, bool ok = false)
        {
            _status.Text = text;
            _status.ForeColor = error ? ErrorColor : ok ? Theme.Text : Theme.Muted;
        }

        private static Color ErrorColor => Theme.Current.IsDark ? Color.FromArgb(0xEE, 0x5A, 0x43) : Color.FromArgb(0xD2, 0x3B, 0x24);

        // ---------- Montagem ----------

        private static Label Section(string text) => new Label
        {
            Text = text.ToUpperInvariant(),
            Font = Theme.Section,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = Padding.Empty
        };

        /// <summary>Linhas "rótulo | opções" alinhadas.</summary>
        private static TableLayoutPanel OptionGrid(params (string Label, Control Control)[] rows)
        {
            var grid = new TableLayoutPanel { ColumnCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (label, control) in rows)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                grid.Controls.Add(new Label { Text = label, ForeColor = Theme.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, grid.RowCount);
                grid.Controls.Add(control, 1, grid.RowCount);
                grid.RowCount++;
            }
            return grid;
        }

        /// <summary>Grade de três colunas iguais com as alturas de linha informadas.</summary>
        private static TableLayoutPanel Grid(int columns, float[] rowHeights)
        {
            var grid = new TableLayoutPanel { ColumnCount = columns, RowCount = rowHeights.Length, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Background };
            for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            foreach (float h in rowHeights) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            return grid;
        }
    }

    /// <summary>
    /// Retângulo da área com os quatro campos encostados nos lados que eles definem. Norte e Sul (latitudes, ou N
    /// em UTM) são as linhas horizontais, em azul; Oeste e Leste (longitudes, ou E) são as verticais, em amarelo,
    /// com o título de cada campo na cor do seu lado. Cada canto mostra a coordenada que sai dos dois lados que se
    /// encontram nele, atualizada enquanto o usuário digita.
    /// </summary>
    internal class SidesPanel : Panel
    {
        private readonly LabeledInput _north, _south, _east, _west;
        private bool _geographic = true;

        // Medidas lógicas (96 dpi)
        private const int FieldHeight = 50;   // Título (20) + campo (28) + folga
        private const int InputCenter = 34;   // Do topo do LabeledInput ao meio do campo de texto
        private const int SideWidth = 150;    // Campos Oeste e Leste
        private const int TopWidth = 200;     // Campos Norte e Sul
        private const int Gap = 16;           // Entre o campo e o retângulo

        public static Color LatitudeColor => Theme.Accent;
        public static Color LongitudeColor => Theme.Current.IsDark ? Color.FromArgb(0xF5, 0xC2, 0x42) : Color.FromArgb(0xA8, 0x6E, 0x00);

        public SidesPanel(LabeledInput north, LabeledInput south, LabeledInput east, LabeledInput west)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            (_north, _south, _east, _west) = (north, south, east, west);
            foreach (LabeledInput field in new[] { north, south, east, west })
            {
                field.Margin = Padding.Empty;
                field.Box.Input.TextChanged += (s, e) => Invalidate();
                Controls.Add(field);
            }
            north.TitleColor = south.TitleColor = LatitudeColor;
            east.TitleColor = west.TitleColor = LongitudeColor;
        }

        /// <summary>Lat/long (true) ou UTM: muda a legenda e o texto dos cantos.</summary>
        public bool Geographic
        {
            get => _geographic;
            set { _geographic = value; Invalidate(); }
        }

        private int S(int v) => Theme.Scale(this, v);

        /// <summary>O retângulo desenhado, entre os quatro campos.</summary>
        private Rectangle Box
        {
            get
            {
                int x = S(SideWidth + Gap), y = S(FieldHeight) + S(Gap) / 2;
                return new Rectangle(x, y, Math.Max(S(40), Width - 2 * x), Math.Max(S(40), Height - 2 * y));
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Rectangle box = Box;
            int fh = S(FieldHeight), tw = S(TopWidth), sw = S(SideWidth);
            int middle = box.Y + box.Height / 2 - S(InputCenter); // Campo de texto na altura do meio do retângulo
            _north.SetBounds((Width - tw) / 2, 0, tw, fh);
            _south.SetBounds((Width - tw) / 2, Height - fh, tw, fh);
            _west.SetBounds(0, middle, sw, fh);
            _east.SetBounds(Width - sw, middle, sw, fh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            Rectangle box = Box;
            float line = S(3);

            using (var fill = new SolidBrush(Color.FromArgb(Theme.Current.IsDark ? 34 : 24, Theme.Accent)))
                g.FillRectangle(fill, box);

            // Lados: horizontais = latitude (Norte/Sul), verticais = longitude (Oeste/Leste)
            using (var lat = new Pen(LatitudeColor, line))
            using (var lon = new Pen(LongitudeColor, line))
            {
                g.DrawLine(lon, box.Left, box.Top, box.Left, box.Bottom);
                g.DrawLine(lon, box.Right, box.Top, box.Right, box.Bottom);
                g.DrawLine(lat, box.Left, box.Top, box.Right, box.Top);
                g.DrawLine(lat, box.Left, box.Bottom, box.Right, box.Bottom);
            }

            // Tracejado ligando cada campo ao seu lado
            using (var lat = new Pen(LatitudeColor, 1.2f) { DashStyle = DashStyle.Dot })
            using (var lon = new Pen(LongitudeColor, 1.2f) { DashStyle = DashStyle.Dot })
            {
                int cx = box.Left + box.Width / 2;
                g.DrawLine(lat, cx, _north.Bottom - S(2), cx, box.Top - line);
                g.DrawLine(lat, cx, box.Bottom + line, cx, _south.Top + S(2));
                int cy = _west.Top + S(InputCenter);
                g.DrawLine(lon, _west.Right + S(2), cy, box.Left - line, cy);
                g.DrawLine(lon, box.Right + line, cy, _east.Left - S(2), cy);
            }

            // Cantos: a coordenada que resulta dos dois lados que se encontram ali
            Corner(g, box.Left, box.Top, _north, _west, right: false, bottom: false);
            Corner(g, box.Right, box.Top, _north, _east, right: true, bottom: false);
            Corner(g, box.Left, box.Bottom, _south, _west, right: false, bottom: true);
            Corner(g, box.Right, box.Bottom, _south, _east, right: true, bottom: true);

            TextRenderer.DrawText(g, "ÁREA DAS RUAS", Theme.Section, box, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            NorthArrow(g, this, Width - S(16), S(6));
            Legend(g, new Point(0, Height - S(38)));
        }

        /// <summary>Ponto no canto e, do lado de dentro, a latitude (ou N) e a longitude (ou E) que ele recebe.</summary>
        private void Corner(Graphics g, int x, int y, LabeledInput latitudeSide, LabeledInput longitudeSide, bool right, bool bottom)
        {
            using (var dot = new SolidBrush(Theme.TextStrong)) g.FillEllipse(dot, x - S(4), y - S(4), S(8), S(8));

            string a = Geographic ? "lat " + Value(latitudeSide) : "N " + Value(latitudeSide);
            string b = Geographic ? "long " + Value(longitudeSide) : "E " + Value(longitudeSide);
            int lineHeight = Theme.Small.Height;
            int width = S(118), pad = S(8);
            int left = right ? x - pad - width : x + pad;
            int top = bottom ? y - pad - 2 * lineHeight : y + pad;
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
                                    (right ? TextFormatFlags.Right : TextFormatFlags.Left);
            TextRenderer.DrawText(g, a, Theme.Small, new Rectangle(left, top, width, lineHeight), LatitudeColor, flags);
            TextRenderer.DrawText(g, b, Theme.Small, new Rectangle(left, top + lineHeight, width, lineHeight), LongitudeColor, flags);
        }

        /// <summary>O valor como foi digitado no campo; "?" enquanto estiver vazio ou não for um número.</summary>
        private string Value(LabeledInput field)
        {
            double? v = Geographic ? Coordinates.ParseDegrees(field.Value, ReferenceEquals(field, _north) || ReferenceEquals(field, _south))
                                   : Coordinates.ParseMeters(field.Value);
            return v == null ? "?" : field.Value;
        }

        private void Legend(Graphics g, Point at)
        {
            int lineHeight = Theme.Small.Height + S(3);
            (string Text, Color Color)[] items = Geographic
                ? new[] { ("latitude: Norte e Sul", LatitudeColor), ("longitude: Oeste e Leste", LongitudeColor) }
                : new[] { ("N (m): Norte e Sul", LatitudeColor), ("E (m): Oeste e Leste", LongitudeColor) };
            for (int i = 0; i < items.Length; i++)
            {
                int y = at.Y + i * lineHeight;
                using (var pen = new Pen(items[i].Color, S(3))) g.DrawLine(pen, at.X, y + lineHeight / 2, at.X + S(16), y + lineHeight / 2);
                TextRenderer.DrawText(g, items[i].Text, Theme.Small, new Point(at.X + S(22), y), Theme.Muted, TextFormatFlags.NoPadding);
            }
        }

        /// <summary>Seta do norte com o "N" em cima, centrada em <paramref name="x"/>.</summary>
        public static void NorthArrow(Graphics g, Control control, int x, int y)
        {
            int S(int v) => Theme.Scale(control, v);
            TextRenderer.DrawText(g, "N", Theme.Section, new Rectangle(x - S(8), y, S(16), S(14)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            using (var pen = new Pen(Theme.Muted, 1.4f) { EndCap = LineCap.ArrowAnchor })
                g.DrawLine(pen, x, y + S(32), x, y + S(16));
        }
    }

    /// <summary>Desenho do modo centro e raio: o quadrado em volta do ponto, com o raio marcado.</summary>
    internal class CenterDiagram : Control
    {
        public CenterDiagram()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Margin = new Padding(8, 6, 8, 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int S(int v) => Theme.Scale(this, v);

            float side = Math.Min(Height - S(8), S(100));
            var box = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
            using (var fill = new SolidBrush(Color.FromArgb(Theme.Current.IsDark ? 34 : 24, Theme.Accent)))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(Theme.Accent, 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);

            var center = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
            using (var brush = new SolidBrush(Theme.TextStrong)) g.FillEllipse(brush, center.X - S(4), center.Y - S(4), S(8), S(8));
            using (var pen = new Pen(Theme.Accent, 1.4f) { EndCap = LineCap.ArrowAnchor }) g.DrawLine(pen, center, new PointF(box.Right - 1, center.Y));
            TextRenderer.DrawText(g, "raio", Theme.Small, new Rectangle((int)center.X, (int)center.Y - S(18), (int)(box.Width / 2), S(14)),
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "centro", Theme.Small, new Rectangle((int)box.X, (int)center.Y + S(6), (int)(box.Width / 2) + S(10), S(14)),
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            SidesPanel.NorthArrow(g, this, (int)box.Right + S(18), (int)box.Y);
        }
    }
}
