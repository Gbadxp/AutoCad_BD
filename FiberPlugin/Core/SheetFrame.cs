using System.Globalization;

namespace FiberPlugin.Core
{
    /// <summary>O que vai na legenda e nos quadros das folhas além do que sai da divisão (folha, escala, formato, coordenadas).</summary>
    public sealed class SheetStamp
    {
        public string Company { get; set; } = "";   // Razão social (Configuração de Projeto, aba Empresa)
        public string Engineer { get; set; } = "";  // Responsável técnico (o representante da aba Empresa)
        public string Crea { get; set; } = "";
        public string Art { get; set; } = "";
        public string Title { get; set; } = "";     // Percurso da rede ou, sem ele, o nome do desenho
        public string Content { get; set; } = "";   // Conteúdo (ex.: PROJETO DE REDE ÓPTICA)
        public string Area { get; set; } = "";      // ProjectInfo.Rural, ProjectInfo.Urban ou vazio
        public string Date { get; set; } = "";
        public string Client { get; set; } = "";
        public string ClientDocument { get; set; } = "";
        public string Address { get; set; } = "";
        public string City { get; set; } = "";
        public string ZipCode { get; set; } = "";
        public string ConsumerUnit { get; set; } = "";
        public string WorkNumber { get; set; } = "";
        public string Approver { get; set; } = "";
        public string Checker { get; set; } = "";
        public string CheckerEmail { get; set; } = "";
        public string Drafter { get; set; } = "";
        public string DrafterEmail { get; set; } = "";
        public string Revision { get; set; } = "";
        public string RevisionReason { get; set; } = "";
        public IReadOnlyList<string> Notes { get; set; } = new string[0];

        /// <summary>Zona UTM do projeto, para o fuso das coordenadas; null sem zona.</summary>
        public UtmSettings? Zone { get; set; }

        /// <summary>Largura / altura da logo e da assinatura; 0 = sem a imagem.</summary>
        public double LogoAspect { get; set; }
        public double SignatureAspect { get; set; }
    }

    public enum PaperAnchor { TopLeft, MiddleLeft, BottomLeft, MiddleCenter, MiddleRight }

    /// <summary>
    /// Texto no papel: posição do ponto de encaixe, altura da letra, largura (0 = uma linha só, sem quebrar) e giro (radianos,
    /// anti-horário). "\n" separa parágrafos.
    /// </summary>
    public sealed class PaperText
    {
        public string Text { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Height { get; set; }
        public double Width { get; set; }
        public double Rotation { get; set; }
        public PaperAnchor Anchor { get; set; }
    }

    /// <summary>Linha no papel; Weight = espessura de impressão em mm (0 = a da layer).</summary>
    public readonly struct PaperLine
    {
        public PaperLine(double x1, double y1, double x2, double y2, double weight = 0)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Weight = weight;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double Weight { get; }
    }

    public enum PaperImage { Logo, Signature }

    /// <summary>Moldura de uma folha em mm no papel (origem no canto de baixo à esquerda), pronta para virar entidades.</summary>
    public sealed class SheetPaper
    {
        public List<PaperLine> Lines { get; } = new List<PaperLine>();
        public List<PaperText> Texts { get; } = new List<PaperText>();

        /// <summary>Retângulos fechados com a largura do traço (quadro, contorno da legenda e dos quadros de texto).</summary>
        public List<(Rect Rect, double Width)> Boxes { get; } = new List<(Rect, double)>();

        /// <summary>Áreas cheias (as marcas de corte dos cantos).</summary>
        public List<Rect> Fills { get; } = new List<Rect>();

        /// <summary>Onde vão a logo e a assinatura, já na proporção de cada uma.</summary>
        public List<(Rect Rect, PaperImage Image)> Images { get; } = new List<(Rect, PaperImage)>();

        /// <summary>O que sobra da coluna da direita para o norte e a legenda dos símbolos (as notas podem ficar embaixo dela).</summary>
        public Rect LegendArea { get; set; }

        /// <summary>As notas técnicas não couberam na folha (A4 deitada).</summary>
        public bool NotesLeftOut { get; set; }
    }

    /// <summary>
    /// Moldura das folhas do Gerar Folhas pela NBR 16752:2020 (folhas de desenho), no modelo de prancha da empresa:
    /// <list type="bullet">
    /// <item>quadro com margem de 20 mm à esquerda e 10 mm nas outras, na largura de linha da tabela 4;</item>
    /// <item>malha de referência (letras e números na faixa de 5 mm em volta do quadro), marcas de centro e de corte;</item>
    /// <item>legenda de 180 mm no canto inferior direito: alterações (aprovação, verificação, desenho, revisão), responsável
    /// técnico com CREA e assinatura, coordenadas UTM do centro da folha, conteúdo, área rural ou urbana, escala, data,
    /// projeto, cliente, U.C., logo, nº da obra e folha;</item>
    /// <item>à esquerda da legenda, as notas técnicas e as observações; na margem esquerda, os avisos da Lei 5.194/66 e da
    /// propriedade dos desenhos;</item>
    /// <item>marcas de dobramento da figura 21 (cópia dobrada em A4 com a legenda à vista).</item>
    /// </list>
    /// </summary>
    public static class SheetFrame
    {
        // Legenda: duas fileiras de blocos com seis linhas de 7 mm. Em cima, alterações e responsável técnico; embaixo,
        // projeto e cliente, e logo e folha. As larguras dos blocos são proporções, esticadas até os 180 mm
        public const double RowHeight = 7;
        private const int Rows = 6;
        public const double BlockHeight = RowHeight * Rows;
        private const double ChangesWidth = 50, ResponsibleWidth = 110, ClientWidth = 75, LogoWidth = 40;

        private const double TextHeight = 2.0;   // mm: campos da legenda ("CLIENTE: ...")
        private const double SmallHeight = 1.6;  // mm: e-mails, títulos de quadro, nome da empresa
        private const double MinHeight = 1.4;    // mm: menor letra quando o texto não cabe
        private const double TitleHeight = 3.5;  // mm: nome do desenho, à esquerda da legenda
        private const double Pad = 1.2;          // mm: da borda do quadro ao texto
        private const double NotesMinWidth = 180; // mm: abaixo disso as notas vão para a coluna da legenda dos símbolos

        private const double GridBand = 5, GridField = 50, GridText = 3.5; // Malha de referência (NBR 16752, 4.4)
        private const double CenterMarkInside = 10;                       // Marcas de centro: 10 mm para dentro do quadro (4.3)
        private const double TrimLong = 10, TrimShort = 5;                // Marcas de corte em L (4.5)
        private const double FoldWeight = 0.18, CornerFoldMark = 8;      // Marcas de dobramento, tracejadas (4.6)
        private const string GridLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";    // Sem I e O

        /// <param name="center">Centro do pedaço do desenho que a folha mostra (coordenadas do desenho, UTM).</param>
        public static SheetPaper Build(SheetPlan plan, int number, string title, SheetStamp stamp, (double X, double Y) center)
        {
            var paper = new SheetPaper();
            SheetFormat format = plan.Format;
            bool landscape = plan.Landscape;
            double width = format.PaperWidth(landscape), height = format.PaperHeight(landscape);
            Rect frame = format.Frame(landscape);
            Rect block = format.TitleBlock(landscape);

            paper.Boxes.Add((frame, format.FrameLineWidth));
            paper.Lines.Add(new PaperLine(frame.MinX, block.MaxY, frame.MaxX, block.MaxY));
            Legend(paper, block, plan, number, stamp, center);

            // À esquerda da legenda: as notas técnicas e as observações; sem espaço para elas, o nome do desenho, e as notas
            // vão para o pé da coluna da legenda dos símbolos
            paper.LegendArea = format.LegendArea(landscape);
            var left = new Rect(frame.MinX, block.MinY, block.MinX, block.MaxY);
            if (left.Width >= NotesMinWidth && stamp.Notes.Count > 0)
            {
                NotesBox(paper, left, stamp.Notes);
            }
            else
            {
                if (left.Width >= 30)
                {
                    paper.Lines.Add(new PaperLine(block.MinX, block.MinY, block.MinX, block.MaxY));
                    double textWidth = left.Width - 8;
                    paper.Texts.Add(Text(title, left.MinX + 4, left.MaxY - 4, Fit(title, TitleHeight, textWidth, 16, 3, 2.5), textWidth, PaperAnchor.TopLeft));
                }
                if (stamp.Notes.Count > 0) paper.NotesLeftOut = !NotesInColumn(paper, stamp.Notes);
            }

            var (_, horizontalFolds, corner) = Folds(width, height);
            ReferenceGrid(paper, format, landscape, frame);
            CenterMarks(paper, frame);
            MarginNotices(paper, frame, horizontalFolds, corner);
            FoldMarks(paper, width, height, frame);
            TrimMarks(paper, width, height);
            return paper;
        }

        /// <summary>"01/24" (três algarismos a partir de 100 folhas).</summary>
        public static string Sheet(int number, int total)
        {
            string digits = total >= 100 ? "D3" : "D2";
            return number.ToString(digits, CultureInfo.InvariantCulture) + "/" + total.ToString(digits, CultureInfo.InvariantCulture);
        }

        /// <summary>"FUSO 20L": a zona e a letra da faixa de latitude (de 8 em 8 graus, de C a X) do ponto.</summary>
        public static string ZoneLabel(UtmSettings zone, double easting, double northing)
        {
            const string bands = "CDEFGHJKLMNPQRSTUVWX";
            double lat = UtmZone.ToGeographic(easting, northing, zone.Zone, zone.South).Lat;
            int band = Math.Max(0, Math.Min(bands.Length - 1, (int)Math.Floor((lat + 80) / 8)));
            return $"FUSO {zone.Zone}{bands[band]}";
        }

        // ---------- Legenda ----------

        private static void Legend(SheetPaper paper, Rect block, SheetPlan plan, int number, SheetStamp stamp, (double X, double Y) center)
        {
            paper.Boxes.Add((block, 0.35));
            double middle = block.MinY + BlockHeight;
            paper.Lines.Add(new PaperLine(block.MinX, middle, block.MaxX, middle));
            var top = new Cells(block.MinX, block.MaxY, block.Width / (ChangesWidth + ResponsibleWidth));
            Changes(paper, top, 0, stamp);
            Responsible(paper, top, ChangesWidth, plan, stamp, center);
            var bottom = new Cells(block.MinX, middle, block.Width / (ClientWidth + LogoWidth));
            Client(paper, bottom, 0, stamp);
            Logo(paper, bottom, ClientWidth, plan, number, stamp);
        }

        /// <summary>Quadro de alterações: título, aprovação, verificação e desenho (com o e-mail embaixo), revisão e motivo.</summary>
        private static void Changes(SheetPaper paper, Cells g, double x0, SheetStamp stamp)
        {
            double w = ChangesWidth;
            for (int r = 1; r < Rows; r++) g.HLine(paper, x0, x0 + w, r);
            g.Center(paper, "ALTERAÇÕES", x0, w, 0, 1, 2.2);
            g.Field(paper, x0, w, 1, "APROV.: " + stamp.Approver);
            g.Person(paper, x0, w, 2, "VERIFI.: ", stamp.Checker, stamp.CheckerEmail);
            g.Person(paper, x0, w, 3, "DESEN.: ", stamp.Drafter, stamp.DrafterEmail);
            g.Field(paper, x0, w, 4, "REVISÃO: " + stamp.Revision);
            g.Field(paper, x0, w, 5, "MOTIVO ALTERAÇÃO: " + stamp.RevisionReason);
        }

        /// <summary>
        /// Responsável técnico (nome, CREA e ART) com o espaço da assinatura ao lado; coordenadas UTM do centro da folha;
        /// conteúdo e área do projeto; escala e data.
        /// </summary>
        private static void Responsible(SheetPaper paper, Cells g, double x0, SheetPlan plan, SheetStamp stamp, (double X, double Y) center)
        {
            const double nameWidth = 65, contentWidth = 72;
            double w = ResponsibleWidth;
            g.VLine(paper, x0, 0, Rows);
            g.HLine(paper, x0, x0 + nameWidth, 1);
            for (int r = 2; r < Rows; r++) if (r != 4) g.HLine(paper, x0, x0 + w, r);
            g.VLine(paper, x0 + nameWidth, 0, 2);
            g.VLine(paper, x0 + contentWidth, 3, Rows);

            g.Field(paper, x0, nameWidth, 0, "RESPONSÁVEL TÉCNICO: " + stamp.Engineer);
            g.Field(paper, x0, nameWidth, 1, "Nº CREA: " + stamp.Crea + (stamp.Art.Length > 0 ? "     ART: " + stamp.Art : ""));

            // Assinatura: a imagem (se houver) ou o espaço com a linha para assinar
            double sx = x0 + nameWidth;
            paper.Texts.Add(Text("ASS.:", g.X(sx) + Pad, g.Row(0) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            double lineY = g.Row(2) + 2;
            paper.Lines.Add(new PaperLine(g.X(sx + 8), lineY, g.X(x0 + w) - 2, lineY));
            if (stamp.SignatureAspect > 0)
            {
                var room = new Rect(g.X(sx + 8), lineY - 1, g.X(x0 + w) - 2, g.Row(0) - 1);
                paper.Images.Add((FitImage(room, stamp.SignatureAspect), PaperImage.Signature));
            }

            string zone = stamp.Zone != null ? ZoneLabel(stamp.Zone, center.X, center.Y) + "     " : "";
            g.Field(paper, x0, w, 2, $"COORDENADAS (UTM): {zone}X: {Coordinates.Meters(center.X)}     Y: {Coordinates.Meters(center.Y)}");

            // Conteúdo (duas linhas de altura) e área do projeto com as caixas de marcar
            paper.Texts.Add(Text("CONTEÚDO:", g.X(x0) + Pad, g.Row(3) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            g.Center(paper, stamp.Content, x0, contentWidth, 3, 5, 2.5, top: 2.5);
            double ax = x0 + contentWidth;
            paper.Texts.Add(Text("ÁREA DO PROJETO:", g.X(ax) + Pad, g.Row(3) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            CheckBox(paper, g.X(ax) + 4, g.Row(3) - 7.2, "RURAL", stamp.Area == ProjectInfo.Rural);
            CheckBox(paper, g.X(ax) + 4, g.Row(3) - 11.4, "URBANO", stamp.Area == ProjectInfo.Urban);

            g.Field(paper, x0, contentWidth, 5, "ESCALA: 1:" + plan.Scale.ToString(CultureInfo.InvariantCulture));
            g.Field(paper, ax, w - contentWidth, 5, "DATA: " + stamp.Date);
        }

        /// <summary>Projeto (no alto, centralizado), cliente, CPF/CNPJ, endereço, município, CEP e U.C.</summary>
        private static void Client(SheetPaper paper, Cells g, double x0, SheetStamp stamp)
        {
            const double zipWidth = 42;
            double w = ClientWidth;
            for (int r = 1; r < Rows; r++) g.HLine(paper, x0, x0 + w, r);
            g.VLine(paper, x0 + zipWidth, 5, Rows);
            g.Center(paper, stamp.Title, x0, w, 0, 1, 2.2);
            g.Field(paper, x0, w, 1, "CLIENTE: " + stamp.Client);
            g.Field(paper, x0, w, 2, "CPF/CNPJ: " + stamp.ClientDocument);
            g.Field(paper, x0, w, 3, "ENDEREÇO: " + stamp.Address);
            g.Field(paper, x0, w, 4, "MUNICÍPIO: " + stamp.City);
            g.Field(paper, x0, zipWidth, 5, "CEP: " + stamp.ZipCode);
            g.Field(paper, x0 + zipWidth, w - zipWidth, 5, "U.C.: " + stamp.ConsumerUnit);
        }

        /// <summary>Logo com o nome da empresa embaixo, nº da obra e o número da folha em letra grande.</summary>
        private static void Logo(SheetPaper paper, Cells g, double x0, SheetPlan plan, int number, SheetStamp stamp)
        {
            double w = LogoWidth;
            g.VLine(paper, x0, 0, Rows);
            g.HLine(paper, x0, x0 + w, 3);
            g.HLine(paper, x0, x0 + w, 4);

            double companyTop = g.Row(3) + 5.2;
            if (stamp.LogoAspect > 0)
            {
                var room = new Rect(g.X(x0) + Pad, companyTop + 0.6, g.X(x0 + w) - Pad, g.Row(0) - Pad);
                paper.Images.Add((FitImage(room, stamp.LogoAspect), PaperImage.Logo));
            }
            if (stamp.Company.Length > 0)
            {
                double textWidth = g.Width(w) - 2 * Pad;
                paper.Texts.Add(Text(stamp.Company, g.X(x0) + Pad, companyTop, Fit(stamp.Company, SmallHeight, textWidth, 4.6, 2, 1.2), textWidth, PaperAnchor.TopLeft));
            }

            g.Field(paper, x0, w, 3, "Nº OBRA: " + stamp.WorkNumber);
            paper.Texts.Add(Text("FOLHA:", g.X(x0) + Pad, g.Row(4) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            g.Center(paper, Sheet(number, plan.Tiles.Count), x0, w, 4, 6, 5, top: 2);
        }

        private static void CheckBox(SheetPaper paper, double x, double y, string label, bool marked)
        {
            const double size = 2.2;
            var box = new Rect(x, y - size / 2, x + size, y + size / 2);
            paper.Boxes.Add((box, 0));
            if (marked)
            {
                paper.Lines.Add(new PaperLine(box.MinX, box.MinY, box.MaxX, box.MaxY));
                paper.Lines.Add(new PaperLine(box.MinX, box.MaxY, box.MaxX, box.MinY));
            }
            paper.Texts.Add(Text(label, x + size + 1.2, y, TextHeight, 0, PaperAnchor.MiddleLeft));
        }

        /// <summary>Retângulo do tamanho da imagem, na proporção dela, no meio do espaço.</summary>
        private static Rect FitImage(Rect room, double aspect)
        {
            double w = Math.Min(room.Width, room.Height * aspect), h = w / aspect;
            return new Rect(room.CenterX - w / 2, room.CenterY - h / 2, room.CenterX + w / 2, room.CenterY + h / 2);
        }

        /// <summary>
        /// Linhas de uma fileira da legenda, de cima para baixo, com as larguras dos blocos multiplicadas pela escala que
        /// estica a fileira até os 180 mm (o texto que não cabe diminui).
        /// </summary>
        private sealed class Cells
        {
            private readonly double _left, _top, _scale;

            public Cells(double left, double top, double scale)
            {
                _left = left;
                _top = top;
                _scale = scale;
            }

            public double X(double local) => _left + local * _scale;
            public double Width(double local) => local * _scale;
            public double Row(double row) => _top - row * RowHeight;

            public void HLine(SheetPaper paper, double x1, double x2, double row) =>
                paper.Lines.Add(new PaperLine(X(x1), Row(row), X(x2), Row(row)));

            public void VLine(SheetPaper paper, double x, double fromRow, double toRow) =>
                paper.Lines.Add(new PaperLine(X(x), Row(fromRow), X(x), Row(toRow)));

            /// <summary>"RÓTULO: valor" numa linha, no meio da altura da linha; diminui a letra para caber.</summary>
            public void Field(SheetPaper paper, double x, double w, int row, string text)
            {
                double room = Width(w) - 2 * Pad;
                paper.Texts.Add(Text(text, X(x) + Pad, Row(row) - RowHeight / 2, Fit(text, TextHeight, room, RowHeight, 1, MinHeight), 0, PaperAnchor.MiddleLeft));
            }

            /// <summary>Nome na linha de cima e o e-mail embaixo, um pouco para dentro (VERIFI. e DESEN.).</summary>
            public void Person(SheetPaper paper, double x, double w, int row, string label, string name, string email)
            {
                double room = Width(w) - 2 * Pad;
                string text = label + name;
                if (email.Length == 0)
                {
                    Field(paper, x, w, row, text);
                    return;
                }
                paper.Texts.Add(Text(text, X(x) + Pad, Row(row) - 0.9, Fit(text, TextHeight, room, RowHeight, 1, MinHeight), 0, PaperAnchor.TopLeft));
                paper.Texts.Add(Text(email, X(x) + Pad + 3, Row(row) - 4.0, Fit(email, SmallHeight, room - 3, RowHeight, 1, 1.2), 0, PaperAnchor.TopLeft));
            }

            /// <summary>Texto no meio do quadro (centralizado na largura), entre as linhas <paramref name="fromRow"/> e <paramref name="toRow"/>.</summary>
            public void Center(SheetPaper paper, string text, double x, double w, int fromRow, int toRow, double height, double top = 0)
            {
                if (text.Length == 0) return;
                double room = Width(w) - 2 * Pad;
                double y = (Row(fromRow) - top + Row(toRow)) / 2;
                paper.Texts.Add(Text(text, X(x) + Width(w) / 2, y, Fit(text, height, room, (toRow - fromRow) * RowHeight - top, 1, MinHeight), 0, PaperAnchor.MiddleCenter));
            }
        }

        // ---------- Notas e avisos ----------

        /// <summary>Quadro NOTAS TÉCNICAS (numeradas) e, à direita dele, o de observações, na altura da legenda.</summary>
        private static void NotesBox(SheetPaper paper, Rect area, IReadOnlyList<string> notes)
        {
            double obsWidth = Math.Min(70, area.Width * 0.22);
            var notesArea = new Rect(area.MinX, area.MinY, area.MaxX - obsWidth, area.MaxY);
            paper.Lines.Add(new PaperLine(notesArea.MaxX, area.MinY, notesArea.MaxX, area.MaxY));
            paper.Lines.Add(new PaperLine(area.MaxX, area.MinY, area.MaxX, area.MaxY));
            paper.Texts.Add(Text("OBS.:", notesArea.MaxX + Pad, area.MaxY - Pad, TextHeight, 0, PaperAnchor.TopLeft));
            NotesText(paper, notesArea, notes);
        }

        /// <summary>
        /// Notas no pé da coluna da legenda dos símbolos (folhas sem espaço ao lado da legenda). False se não couberem
        /// deixando pelo menos 100 mm para o norte e os símbolos.
        /// </summary>
        private static bool NotesInColumn(SheetPaper paper, IReadOnlyList<string> notes)
        {
            Rect column = paper.LegendArea;
            double needed = 8 + ParagraphsHeight(Numbered(notes), MinHeight + 0.2, column.Width - 2 * Pad) + 2;
            if (column.Height - needed < 100) return false;

            var box = new Rect(column.MinX, column.MinY, column.MaxX, column.MinY + needed);
            paper.Lines.Add(new PaperLine(box.MinX, box.MaxY, box.MaxX, box.MaxY));
            NotesText(paper, box, notes);
            paper.LegendArea = new Rect(column.MinX, box.MaxY, column.MaxX, column.MaxY);
            return true;
        }

        private static void NotesText(SheetPaper paper, Rect area, IReadOnlyList<string> notes)
        {
            paper.Texts.Add(Text("NOTAS TÉCNICAS:", area.CenterX, area.MaxY - 3.5, 2.2, 0, PaperAnchor.MiddleCenter));
            string[] numbered = Numbered(notes);
            double textWidth = area.Width - 2 * Pad, room = area.Height - 7 - Pad;
            double height = 2.5; // Diminui até caber no quadro
            while (height > MinHeight + 1e-9 && ParagraphsHeight(numbered, height, textWidth) > room) height = Math.Round(height - 0.1, 1);
            paper.Texts.Add(Text(string.Join("\n", numbered), area.MinX + Pad, area.MaxY - 7, height, textWidth, PaperAnchor.TopLeft));
        }

        private static string[] Numbered(IReadOnlyList<string> notes) =>
            notes.Select((note, i) => $"{i + 1}. {note}").ToArray();

        /// <summary>Altura estimada dos parágrafos (cada um começa numa linha nova) quebrados na largura.</summary>
        private static double ParagraphsHeight(IEnumerable<string> paragraphs, double height, double width)
        {
            int lines = paragraphs.Sum(p => Math.Max(1, (int)Math.Ceiling(p.Length * CharWidth * height / width)));
            return height * (1 + Descent) + (lines - 1) * height * LineSpacing;
        }

        /// <summary>
        /// Os dois avisos na margem esquerda (fora da malha), em pé, lidos de baixo para cima: a Lei 5.194/66 no alto e a
        /// propriedade dos desenhos embaixo, cada um num quadro do comprimento do texto, sem passar pelas marcas de dobra.
        /// </summary>
        private static void MarginNotices(SheetPaper paper, Rect frame, double[] horizontalFolds, bool corner)
        {
            const double x1 = 1.5, gap = 6;
            double x2 = frame.MinX - GridBand - 1.5;
            double across = x2 - x1 - 2 * Pad; // Espaço para as linhas, na largura da margem

            // Trechos livres da margem, de cima para baixo (as dobras e a ponta da dobra do canto ficam de fora)
            var cuts = horizontalFolds.Select(y => (From: y - 3, To: y + (corner && Math.Abs(y - 297) < 1 ? 12 : 3))).OrderByDescending(c => c.To).ToList();
            var free = new List<(double Bottom, double Top)>();
            double top = frame.MaxY;
            foreach (var (from, to) in cuts)
            {
                if (to < top) free.Add((Math.Max(frame.MinY, to), top));
                top = Math.Min(top, from);
            }
            if (top > frame.MinY) free.Add((frame.MinY, top));

            string[] texts = { SheetNotes.ChangesNotice, SheetNotes.PropertyNotice };
            for (double height = 1.8; height > 1.2 - 1e-9; height = Math.Round(height - 0.1, 1))
            {
                int lines = Math.Max(1, (int)Math.Floor((across - height * (1 + Descent)) / (height * LineSpacing)) + 1);
                double[] lengths = texts.Select(t => t.Length * CharWidth * height / lines + 2 * Pad + 2).ToArray();
                // Lei no trecho mais alto em que couber (encostada em cima), propriedade no mais baixo (encostada embaixo)
                int first = free.FindIndex(f => f.Top - f.Bottom >= lengths[0]);
                if (first < 0) continue;
                var upper = new Rect(x1, free[first].Top - lengths[0], x2, free[first].Top);
                int last = free.FindLastIndex(f => (f.Bottom >= upper.MaxY ? f.Top : Math.Min(f.Top, upper.MinY - gap)) - f.Bottom >= lengths[1]);
                if (last < first) continue;
                var lower = new Rect(x1, free[last].Bottom, x2, free[last].Bottom + lengths[1]);
                foreach (var (box, text) in new[] { (upper, texts[0]), (lower, texts[1]) })
                {
                    paper.Boxes.Add((box, 0));
                    // Girado 90°: o texto corre para cima, a partir do pé do quadro, e as linhas avançam para a direita
                    paper.Texts.Add(new PaperText
                    {
                        Text = text, X = box.MinX + Pad, Y = box.MinY + Pad, Height = height,
                        Width = box.Height - 2 * Pad, Rotation = Math.PI / 2, Anchor = PaperAnchor.TopLeft
                    });
                }
                return;
            }
        }

        private static PaperText Text(string text, double x, double y, double height, double width, PaperAnchor anchor) =>
            new PaperText { Text = text, X = x, Y = y, Height = height, Width = width, Anchor = anchor };

        // Espaço entre linhas do MText (5/3 da altura da letra), o que a letra ocupa abaixo da linha (descendentes) e
        // largura média de uma letra (fonte do estilo Standard, contando com maiúsculas), em alturas de letra
        private const double LineSpacing = 5.0 / 3.0;
        private const double Descent = 0.35;
        private const double CharWidth = 0.8;

        /// <summary>
        /// Maior altura de letra, de <paramref name="height"/> para baixo até <paramref name="minimum"/>, em que o texto cabe
        /// em <paramref name="width"/> mm de largura (em até <paramref name="maxLines"/> linhas, pela largura média das
        /// letras) e em <paramref name="room"/> mm de altura.
        /// </summary>
        public static double Fit(string text, double height, double width, double room, int maxLines, double minimum)
        {
            for (double h = height; h > minimum + 1e-9; h = Math.Round(h - 0.1, 1))
            {
                int lines = (int)Math.Ceiling(text.Length * CharWidth * h / width);
                if (lines <= maxLines && h * (1 + Descent) + (lines - 1) * h * LineSpacing <= room) return h;
            }
            return minimum;
        }

        // ---------- Malha, marcas de centro, de dobramento e de corte (NBR 16752, seção 4) ----------

        /// <summary>
        /// Malha de referência (4.4): faixa de 5 mm em volta do quadro, com campos de 50 mm a partir dos eixos de simetria (a
        /// sobra fica nos campos das pontas) na quantidade da tabela 5; números da esquerda para a direita em cima e embaixo,
        /// letras de cima para baixo dos dois lados (sem I e O), em 3,5 mm; no A4, só em cima e à direita. A identificação
        /// do formato vai no campo de baixo à direita.
        /// </summary>
        private static void ReferenceGrid(SheetPaper paper, SheetFormat format, bool landscape, Rect frame)
        {
            const double weight = 0.35;
            var outer = new Rect(frame.MinX - GridBand, frame.MinY - GridBand, frame.MaxX + GridBand, frame.MaxY + GridBand);
            paper.Lines.Add(new PaperLine(outer.MinX, outer.MinY, outer.MaxX, outer.MinY, weight));
            paper.Lines.Add(new PaperLine(outer.MaxX, outer.MinY, outer.MaxX, outer.MaxY, weight));
            paper.Lines.Add(new PaperLine(outer.MaxX, outer.MaxY, outer.MinX, outer.MaxY, weight));
            paper.Lines.Add(new PaperLine(outer.MinX, outer.MaxY, outer.MinX, outer.MinY, weight));

            var (longFields, shortFields) = format.GridFields;
            double[] xs = Divisions(frame.MinX, frame.MaxX, landscape ? longFields : shortFields);
            double[] ys = Divisions(frame.MinY, frame.MaxY, landscape ? shortFields : longFields);
            bool a4 = format.Name == "A4";
            double topBand = frame.MaxY + GridBand / 2, bottomBand = frame.MinY - GridBand / 2;
            double leftBand = frame.MinX - GridBand / 2, rightBand = frame.MaxX + GridBand / 2;

            for (int i = 0; i < xs.Length - 1; i++)
            {
                if (i > 0)
                {
                    paper.Lines.Add(new PaperLine(xs[i], frame.MaxY, xs[i], outer.MaxY, weight));
                    if (!a4) paper.Lines.Add(new PaperLine(xs[i], outer.MinY, xs[i], frame.MinY, weight));
                }
                string label = (i + 1).ToString(CultureInfo.InvariantCulture);
                double cx = (xs[i] + xs[i + 1]) / 2;
                paper.Texts.Add(Text(label, cx, topBand, GridText, 0, PaperAnchor.MiddleCenter));
                if (!a4) paper.Texts.Add(Text(label, cx, bottomBand, GridText, 0, PaperAnchor.MiddleCenter));
            }
            // Letras de cima para baixo
            int fields = ys.Length - 1;
            for (int i = 0; i < fields; i++)
            {
                double y = ys[fields - i]; // Alto do campo
                if (i > 0)
                {
                    paper.Lines.Add(new PaperLine(frame.MaxX, y, outer.MaxX, y, weight));
                    if (!a4) paper.Lines.Add(new PaperLine(outer.MinX, y, frame.MinX, y, weight));
                }
                string letter = GridLetter(i);
                double cy = (ys[fields - i] + ys[fields - i - 1]) / 2;
                paper.Texts.Add(Text(letter, rightBand, cy, GridText, 0, PaperAnchor.MiddleCenter));
                if (!a4) paper.Texts.Add(Text(letter, leftBand, cy, GridText, 0, PaperAnchor.MiddleCenter));
            }

            paper.Texts.Add(Text(format.Name, frame.MaxX - 1, bottomBand, GridText, 0, PaperAnchor.MiddleRight));
        }

        /// <summary>Divisões de um lado do quadro: de 50 em 50 mm a partir do meio, com as pontas ficando com a sobra.</summary>
        public static double[] Divisions(double start, double end, int fields)
        {
            double middle = (start + end) / 2;
            var marks = new List<double> { start, end };
            for (int k = 0; k < fields / 2; k++)
            {
                marks.Add(middle + k * GridField);
                if (k > 0) marks.Add(middle - k * GridField);
            }
            return marks.Where(m => m >= start && m <= end).Distinct().OrderBy(m => m).ToArray();
        }

        /// <summary>A, B, ... Z (sem I e O); depois AA, AB...</summary>
        private static string GridLetter(int index) =>
            index < GridLetters.Length ? GridLetters[index].ToString() : "A" + GridLetters[index - GridLetters.Length];

        /// <summary>Marcas de centro (4.3): nas pontas dos eixos de simetria, da malha até 10 mm para dentro do quadro, em 0,7 mm.</summary>
        private static void CenterMarks(SheetPaper paper, Rect frame)
        {
            const double weight = 0.7;
            double cx = frame.CenterX, cy = frame.CenterY;
            paper.Lines.Add(new PaperLine(cx, frame.MaxY + GridBand, cx, frame.MaxY - CenterMarkInside, weight));
            paper.Lines.Add(new PaperLine(cx, frame.MinY - GridBand, cx, frame.MinY + CenterMarkInside, weight));
            paper.Lines.Add(new PaperLine(frame.MinX - GridBand, cy, frame.MinX + CenterMarkInside, cy, weight));
            paper.Lines.Add(new PaperLine(frame.MaxX + GridBand, cy, frame.MaxX - CenterMarkInside, cy, weight));
        }

        /// <summary>
        /// Dobramento da folha (NBR 16752, seção 7 e figura 21) para a cópia dobrada ficar no formato A4 com a legenda à vista,
        /// em mm a partir do canto de baixo à esquerda. As dobras verticais vêm primeiro, a partir da direita: o painel da
        /// frente (a legenda de 180 mm e a margem de 10 mm) e os outros da figura 21; a sobra à esquerda tem 210 mm, com a
        /// margem de furar (125 mm no A3). Depois as horizontais, de 297 em 297 mm a partir de baixo. A0, A1 e A2 também têm o
        /// canto superior esquerdo dobrado para trás (de 105 mm no alto até a dobra de 297 mm). A folha em pé segue as mesmas
        /// medidas pela largura dela.
        /// </summary>
        public static (double[] Vertical, double[] Horizontal, bool Corner) Folds(double width, double height)
        {
            if (Math.Max(width, height) <= 297.5) return (new double[0], new double[0], false); // A4 já é o formato final

            double[] panels = Math.Round(width) switch
            {
                1189.0 => new[] { 190, 109.5, 109.5, 190, 190, 190 },
                841.0 => new[] { 190, 125.5, 125.5, 190 },
                594.0 => new[] { 192, 192.0 },
                420.0 => new[] { 190, 105.0 },
                297.0 => new[] { 190.0 },
                _ => new double[0]
            };
            var vertical = new List<double>();
            double fromRight = 0;
            foreach (double panel in panels)
            {
                fromRight += panel;
                vertical.Add(width - fromRight);
            }

            var horizontal = new List<double>();
            for (double y = 297; y < height - 0.5; y += 297) horizontal.Add(y);
            return (vertical.ToArray(), horizontal.ToArray(), Math.Max(width, height) >= 593.5);
        }

        /// <summary>
        /// Marcas de dobramento (4.6): tracejadas, em 0,18 mm, na margem, entre a borda do papel e a malha. A dobra do canto,
        /// de 105 mm no alto até a borda esquerda na altura de 297 mm, aparece pelas duas pontas.
        /// </summary>
        private static void FoldMarks(SheetPaper paper, double width, double height, Rect frame)
        {
            var (vertical, horizontal, corner) = Folds(width, height);
            double bottom = frame.MinY - GridBand, top = frame.MaxY + GridBand, left = frame.MinX - GridBand, right = frame.MaxX + GridBand;
            foreach (double x in vertical)
            {
                Dashed(paper, x, 0, x, bottom);
                Dashed(paper, x, height, x, top);
            }
            foreach (double y in horizontal)
            {
                Dashed(paper, 0, y, left, y);
                Dashed(paper, width, y, right, y);
            }
            if (!corner) return;

            double dx = 0 - 105, dy = 297 - height, length = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length, uy = dy / length;
            double toBand = (height - top) / -uy; // Do alto do papel até a malha
            Dashed(paper, 105, height, 105 + ux * toBand, height + uy * toBand);
            Dashed(paper, 0, 297, -ux * CornerFoldMark, 297 - uy * CornerFoldMark);
        }

        /// <summary>Linha tracejada (traços de 2 mm, espaços de 1 mm) feita de pedaços, sem depender do tipo de linha do desenho.</summary>
        private static void Dashed(SheetPaper paper, double x1, double y1, double x2, double y2)
        {
            const double dash = 2, space = 1;
            double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (length < 1e-6) return;
            double ux = (x2 - x1) / length, uy = (y2 - y1) / length;
            for (double s = 0; s < length; s += dash + space)
            {
                double e = Math.Min(length, s + dash);
                paper.Lines.Add(new PaperLine(x1 + ux * s, y1 + uy * s, x1 + ux * e, y1 + uy * e, FoldWeight));
            }
        }

        /// <summary>Marcas de corte (4.5): nos quatro cantos do papel, um L cheio de dois retângulos de 10 x 5 mm.</summary>
        private static void TrimMarks(SheetPaper paper, double width, double height)
        {
            foreach (var (x, y, sx, sy) in new[] { (0.0, 0.0, 1, 1), (width, 0.0, -1, 1), (0.0, height, 1, -1), (width, height, -1, -1) })
            {
                paper.Fills.Add(Span(x, y, x + sx * TrimLong, y + sy * TrimShort));
                paper.Fills.Add(Span(x, y, x + sx * TrimShort, y + sy * TrimLong));
            }
        }

        private static Rect Span(double x1, double y1, double x2, double y2) =>
            new Rect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }
}
