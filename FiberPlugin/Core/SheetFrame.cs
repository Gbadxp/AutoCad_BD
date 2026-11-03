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

        /// <summary>Onde vão o norte e a legenda dos símbolos: o alto da coluna da direita.</summary>
        public Rect LegendArea { get; set; }
    }

    /// <summary>
    /// Moldura das folhas do Gerar Folhas pela NBR 16752:2020 (folhas de desenho), no modelo de prancha da empresa:
    /// <list type="bullet">
    /// <item>quadro com margem de 20 mm à esquerda e 10 mm nas outras, na largura de linha da tabela 4;</item>
    /// <item>malha de referência (letras e números na faixa de 5 mm em volta do quadro), marcas de centro e de corte;</item>
    /// <item>legenda de 180 mm deitada no canto inferior direito (com o norte e os símbolos na coluna da direita, acima):
    /// logo e empresa, projeto, cliente, CPF/CNPJ, endereço, município, CEP, U.C., responsável técnico com CREA e assinatura,
    /// coordenadas UTM do centro da folha, conteúdo, área rural ou urbana, escala, data, nº da obra e folha;</item>
    /// <item>na margem esquerda, os avisos da Lei 5.194/66 e da propriedade dos desenhos;</item>
    /// <item>marcas de dobramento da figura 21 (cópia dobrada em A4 com a legenda à vista).</item>
    /// </list>
    /// </summary>
    public static class SheetFrame
    {
        private const double TextHeight = 2.0;   // mm: campos da legenda ("CLIENTE: ...")
        private const double SmallHeight = 1.6;  // mm: títulos de quadro
        private const double MinHeight = 1.4;    // mm: menor letra quando o texto não cabe
        private const double TitleHeight = 3.5;  // mm: nome do desenho, à esquerda da legenda (folha estreita)
        private const double Pad = 1.2;          // mm: da borda do quadro ao texto

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
            paper.LegendArea = format.LegendArea(landscape);

            // A legenda numa faixa embaixo e, à esquerda dela (se couber), o nome do desenho
            paper.Lines.Add(new PaperLine(frame.MinX, block.MaxY, frame.MaxX, block.MaxY));
            var left = new Rect(frame.MinX, block.MinY, block.MinX, block.MaxY);
            if (left.Width >= 30)
            {
                double textWidth = left.Width - 8;
                paper.Texts.Add(Text(title, left.MinX + 4, left.MaxY - 4, Fit(title, TitleHeight, textWidth, left.Height - 8, 3, 2.5), textWidth, PaperAnchor.TopLeft));
            }
            Legend(paper, block, plan, number, stamp, center);

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

        /// <summary>
        /// Legenda deitada, em dois blocos de sete linhas lado a lado: identificação (logo e empresa, projeto, cliente,
        /// endereço, U.C.) e o técnico (responsável técnico com a assinatura, coordenadas, conteúdo e área, escala, data, nº da
        /// obra e a folha, no canto de baixo à direita).
        /// </summary>
        private static void Legend(SheetPaper paper, Rect block, SheetPlan plan, int number, SheetStamp stamp, (double X, double Y) center)
        {
            double middle = block.MinX + block.Width / 2;
            var identity = new Rect(block.MinX, block.MinY, middle, block.MaxY);
            var technical = new Rect(middle, block.MinY, block.MaxX, block.MaxY);
            paper.Boxes.Add((identity, 0));
            paper.Boxes.Add((technical, 0));
            paper.Boxes.Add((block, 0.35));
            Identity(paper, identity, stamp);
            Technical(paper, technical, plan, number, stamp, center);
        }

        /// <summary>Logo com o nome da empresa ao lado, o projeto, cliente e CPF/CNPJ, endereço, município, CEP e U.C.</summary>
        private static void Identity(SheetPaper paper, Rect r, SheetStamp stamp)
        {
            var g = new Cells(r.MinX, r.MaxY);
            double w = r.Width, logo = Math.Round(w * 0.38);
            g.VLine(paper, logo, 0, 3);
            for (int row = 3; row < 7; row++) g.HLine(paper, 0, w, row);

            if (stamp.LogoAspect > 0)
            {
                var room = new Rect(r.MinX + Pad, g.Row(3) + Pad, r.MinX + logo - Pad, g.Row(0) - Pad);
                paper.Images.Add((FitImage(room, stamp.LogoAspect), PaperImage.Logo));
            }
            if (stamp.Company.Length > 0)
            {
                double textWidth = w - logo - 2 * Pad;
                paper.Texts.Add(Text(stamp.Company, r.MinX + logo + (w - logo) / 2, g.Row(1.5),
                    Fit(stamp.Company, 2.2, textWidth, 3 * SheetFormat.LegendRow - 2, 3, MinHeight), textWidth, PaperAnchor.MiddleCenter));
            }

            g.Center(paper, stamp.Title, 0, w, 3, 4, 2.4);
            double client = Math.Round(w * 0.6), city = Math.Round(w * 0.48), zip = Math.Round(w * 0.24);
            g.VLine(paper, client, 4, 5);
            g.Field(paper, 0, client, 4, "CLIENTE: " + stamp.Client);
            g.Field(paper, client, w - client, 4, "CPF/CNPJ: " + stamp.ClientDocument);
            g.Field(paper, 0, w, 5, "ENDEREÇO: " + stamp.Address);
            g.VLine(paper, city, 6, 7);
            g.VLine(paper, city + zip, 6, 7);
            g.Field(paper, 0, city, 6, "MUNICÍPIO: " + stamp.City);
            g.Field(paper, city, zip, 6, "CEP: " + stamp.ZipCode);
            g.Field(paper, city + zip, w - city - zip, 6, "U.C.: " + stamp.ConsumerUnit);
        }

        /// <summary>
        /// Responsável técnico (nome, CREA e ART) com o espaço da assinatura ao lado; coordenadas UTM do centro da folha;
        /// conteúdo e área do projeto; escala, data e nº da obra, com o número da folha em letra grande no canto.
        /// </summary>
        private static void Technical(SheetPaper paper, Rect r, SheetPlan plan, int number, SheetStamp stamp, (double X, double Y) center)
        {
            var g = new Cells(r.MinX, r.MaxY);
            double w = r.Width, split = Math.Round(w * 0.6), half = Math.Round(split / 2);
            g.VLine(paper, split, 0, 2);
            g.VLine(paper, split, 3, 7);
            g.HLine(paper, 0, w, 2);
            g.HLine(paper, 0, w, 3);
            g.HLine(paper, 0, w, 5);
            g.HLine(paper, 0, split, 6);
            g.VLine(paper, half, 5, 6);

            // Responsável técnico: título do quadro, nome e CREA, uma linha embaixo da outra
            double room = split - 2 * Pad;
            paper.Texts.Add(Text("RESPONSÁVEL TÉCNICO:", r.MinX + Pad, g.Row(0) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            if (stamp.Engineer.Length > 0)
                paper.Texts.Add(Text(stamp.Engineer, r.MinX + Pad, g.Row(0) - 4.3, Fit(stamp.Engineer, TextHeight, room, 4, 1, MinHeight), 0, PaperAnchor.TopLeft));
            string crea = "Nº CREA: " + stamp.Crea + (stamp.Art.Length > 0 ? "   ART: " + stamp.Art : "");
            paper.Texts.Add(Text(crea, r.MinX + Pad, g.Row(0) - 8.9, Fit(crea, 1.8, room, 4, 1, MinHeight), 0, PaperAnchor.TopLeft));

            // Assinatura: a imagem (se houver) ou o espaço com a linha para assinar
            paper.Texts.Add(Text("ASS.:", r.MinX + split + Pad, g.Row(0) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            double lineY = g.Row(2) + 2;
            paper.Lines.Add(new PaperLine(r.MinX + split + 4, lineY, r.MaxX - 3, lineY));
            if (stamp.SignatureAspect > 0)
            {
                var signature = new Rect(r.MinX + split + 4, lineY - 1, r.MaxX - 3, g.Row(0) - 3);
                paper.Images.Add((FitImage(signature, stamp.SignatureAspect), PaperImage.Signature));
            }

            string zone = stamp.Zone != null ? ZoneLabel(stamp.Zone, center.X, center.Y) + "   " : "";
            g.Field(paper, 0, w, 2, $"COORDENADAS (UTM): {zone}X: {Coordinates.Meters(center.X)}   Y: {Coordinates.Meters(center.Y)}");

            // Conteúdo (duas linhas de altura) e área do projeto com as caixas de marcar
            paper.Texts.Add(Text("CONTEÚDO:", r.MinX + Pad, g.Row(3) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            g.Center(paper, stamp.Content, 0, split, 3, 5, 2.5, top: 2.5);
            paper.Texts.Add(Text("ÁREA DO PROJETO:", r.MinX + split + Pad, g.Row(3) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            CheckBox(paper, r.MinX + split + 4, g.Row(3) - 7.2, "RURAL", stamp.Area == ProjectInfo.Rural);
            CheckBox(paper, r.MinX + split + 4, g.Row(3) - 11.4, "URBANO", stamp.Area == ProjectInfo.Urban);

            g.Field(paper, 0, half, 5, "ESCALA: 1:" + plan.Scale.ToString(CultureInfo.InvariantCulture));
            g.Field(paper, half, split - half, 5, "DATA: " + stamp.Date);
            g.Field(paper, 0, split, 6, "Nº OBRA: " + stamp.WorkNumber);
            paper.Texts.Add(Text("FOLHA:", r.MinX + split + Pad, g.Row(5) - Pad, SmallHeight, 0, PaperAnchor.TopLeft));
            g.Center(paper, Sheet(number, plan.Tiles.Count), split, w - split, 5, 7, 5, top: 2);
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

        /// <summary>Linhas de um bloco da legenda, de cima para baixo (x em mm a partir da esquerda do bloco).</summary>
        private sealed class Cells
        {
            private readonly double _left, _top;

            public Cells(double left, double top)
            {
                _left = left;
                _top = top;
            }

            public double Row(double row) => _top - row * SheetFormat.LegendRow;

            public void HLine(SheetPaper paper, double x1, double x2, double row) =>
                paper.Lines.Add(new PaperLine(_left + x1, Row(row), _left + x2, Row(row)));

            public void VLine(SheetPaper paper, double x, double fromRow, double toRow) =>
                paper.Lines.Add(new PaperLine(_left + x, Row(fromRow), _left + x, Row(toRow)));

            /// <summary>"RÓTULO: valor" numa linha, no meio da altura da linha; diminui a letra para caber.</summary>
            public void Field(SheetPaper paper, double x, double w, int row, string text)
            {
                double room = w - 2 * Pad;
                paper.Texts.Add(Text(text, _left + x + Pad, Row(row + 0.5), Fit(text, TextHeight, room, SheetFormat.LegendRow, 1, MinHeight), 0, PaperAnchor.MiddleLeft));
            }

            /// <summary>Texto no meio do quadro (centralizado na largura), entre as linhas <paramref name="fromRow"/> e <paramref name="toRow"/>.</summary>
            public void Center(SheetPaper paper, string text, double x, double w, int fromRow, int toRow, double height, double top = 0)
            {
                if (text.Length == 0) return;
                double room = w - 2 * Pad;
                double y = (Row(fromRow) - top + Row(toRow)) / 2;
                paper.Texts.Add(Text(text, _left + x + w / 2, y, Fit(text, height, room, (toRow - fromRow) * SheetFormat.LegendRow - top, 1, MinHeight), 0, PaperAnchor.MiddleCenter));
            }
        }

        // ---------- Avisos ----------

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
        private const double CharWidth = 0.86;

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
