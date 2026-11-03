using System.Globalization;

namespace FiberPlugin.Core
{
    /// <summary>O que vai no carimbo das folhas além do que sai da divisão (folha, escala, formato).</summary>
    public sealed class SheetStamp
    {
        public string Company { get; set; } = "";   // Razão social (Configuração de Projeto, aba Empresa)
        public string Engineer { get; set; } = "";  // Responsável técnico (o representante da aba Empresa)
        public string Crea { get; set; } = "";
        public string Project { get; set; } = "";   // Percurso da rede ou, sem ele, o nome do desenho
        public string Art { get; set; } = "";
        public string Date { get; set; } = "";

        /// <summary>Largura / altura da logo; 0 = sem logo (o quadro dela fica vazio).</summary>
        public double LogoAspect { get; set; }
    }

    public enum PaperAnchor { TopLeft, BottomLeft, MiddleLeft }

    /// <summary>Texto no papel: posição do ponto de encaixe, altura da letra e largura (0 = uma linha só, sem quebrar).</summary>
    public sealed class PaperText
    {
        public string Text { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Height { get; set; }
        public double Width { get; set; }
        public PaperAnchor Anchor { get; set; }
    }

    /// <summary>Linha no papel; Width &gt; 0 = traço grosso (polilinha com largura).</summary>
    public readonly struct PaperLine
    {
        public PaperLine(double x1, double y1, double x2, double y2, double width = 0)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Width = width;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double Width { get; }
    }

    /// <summary>Moldura de uma folha em mm no papel (origem no canto de baixo à esquerda), pronta para virar entidades.</summary>
    public sealed class SheetPaper
    {
        public List<PaperLine> Lines { get; } = new List<PaperLine>();
        public List<PaperText> Texts { get; } = new List<PaperText>();

        /// <summary>Retângulos fechados com a largura do traço (moldura e contorno do carimbo).</summary>
        public List<(Rect Rect, double Width)> Boxes { get; } = new List<(Rect, double)>();

        /// <summary>Áreas cheias (as marcas de corte dos cantos).</summary>
        public List<Rect> Fills { get; } = new List<Rect>();

        /// <summary>Onde vai a logo, já na proporção dela; null sem logo.</summary>
        public Rect? Logo { get; set; }
    }

    /// <summary>
    /// Moldura das folhas do Gerar Folhas: borda, carimbo no canto de baixo à direita (NBR 16752: logo, empresa, projeto,
    /// responsável técnico com CREA, espaço para a assinatura, data, escala, formato, número da folha e ART), título do
    /// desenho à esquerda do carimbo, marcas de dobra da NBR 13142 nas bordas e marcas de corte nos cantos.
    /// </summary>
    public static class SheetFrame
    {
        private const double LabelHeight = 1.6;  // mm: títulos dos campos do carimbo
        private const double ValueHeight = 2.5;  // mm: valores
        private const double MinHeight = 1.8;    // mm: menor letra de um valor que não cabe
        private const double TitleHeight = 3.5;  // mm: nome do desenho, à esquerda do carimbo
        private const double Pad = 1.5;          // mm: do lado esquerdo e do alto do quadro ao título do campo
        private const double ValueTop = 4.5;     // mm: do alto do quadro ao valor (abaixo do título do campo)
        private const double FoldMark = 5;       // mm: comprimento da marca de dobra, da borda do papel para dentro
        private const double CornerFoldMark = 8; // mm: as duas pontas da dobra do canto superior esquerdo
        private const double TrimLong = 10, TrimShort = 5; // mm: marca de corte em L nos cantos (NBR 16752)

        // Quadros do carimbo (175 de largura): logo | empresa e projeto | responsável e assinatura; embaixo, a linha
        // data | escala | formato | folha | ART
        private const double LogoWidth = 40, MiddleWidth = 70;
        private const double BottomRow = 9;
        private static readonly double[] BottomCells = { 35, 35, 30, 35, 40 };

        public static SheetPaper Build(SheetPlan plan, int number, string title, SheetStamp stamp)
        {
            var paper = new SheetPaper();
            SheetFormat format = plan.Format;
            bool landscape = plan.Landscape;
            double width = format.PaperWidth(landscape), height = format.PaperHeight(landscape);
            Rect frame = format.Frame(landscape);
            Rect block = format.TitleBlock(landscape);

            paper.Boxes.Add((frame, 0.5));
            paper.Lines.Add(new PaperLine(frame.MinX, block.MaxY, frame.MaxX, block.MaxY));

            // Título à esquerda do carimbo (sem espaço, em A4 em pé, o projeto já está no carimbo)
            double left = block.MinX - frame.MinX;
            if (left >= 30)
            {
                paper.Lines.Add(new PaperLine(block.MinX, block.MinY, block.MinX, block.MaxY));
                double textWidth = left - 8;
                paper.Texts.Add(Text(title, frame.MinX + 4, block.MaxY - 4, Fit(title, TitleHeight, textWidth, 16, 3, 2.5), textWidth, PaperAnchor.TopLeft));
                string info = $"FOLHA {Sheet(number, plan.Tiles.Count)}    ESCALA 1:{plan.Scale}    {format.Name}";
                paper.Texts.Add(Text(info, frame.MinX + 4, block.MinY + 4, ValueHeight, 0, PaperAnchor.BottomLeft));
            }

            TitleBlock(paper, block, plan, number, stamp);
            FoldMarks(paper, width, height);
            TrimMarks(paper, width, height);
            return paper;
        }

        /// <summary>"01/24" (três algarismos a partir de 100 folhas).</summary>
        public static string Sheet(int number, int total)
        {
            string digits = total >= 100 ? "D3" : "D2";
            return number.ToString(digits, CultureInfo.InvariantCulture) + "/" + total.ToString(digits, CultureInfo.InvariantCulture);
        }

        private static void TitleBlock(SheetPaper paper, Rect block, SheetPlan plan, int number, SheetStamp stamp)
        {
            double top = block.MaxY, upperBottom = block.MinY + BottomRow, middle = (top + upperBottom) / 2;
            double x1 = block.MinX + LogoWidth, x2 = x1 + MiddleWidth;

            paper.Boxes.Add((block, 0.35));
            paper.Lines.Add(new PaperLine(block.MinX, upperBottom, block.MaxX, upperBottom));
            paper.Lines.Add(new PaperLine(x1, upperBottom, x1, top));
            paper.Lines.Add(new PaperLine(x2, upperBottom, x2, top));
            paper.Lines.Add(new PaperLine(x1, middle, block.MaxX, middle));

            // Logo no meio do quadro dela, na proporção da imagem
            if (stamp.LogoAspect > 0)
            {
                double maxW = LogoWidth - 2 * Pad - 1, maxH = top - upperBottom - 2 * Pad - 1;
                double w = Math.Min(maxW, maxH * stamp.LogoAspect), h = w / stamp.LogoAspect;
                double cx = block.MinX + LogoWidth / 2, cy = (top + upperBottom) / 2;
                paper.Logo = new Rect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
            }

            // Empresa e projeto, cada um em até duas linhas
            double textWidth = MiddleWidth - 2 * Pad - 1;
            Field(paper, x1, top, "EMPRESA", stamp.Company, textWidth, 2.2, middle);
            Field(paper, x1, middle, "PROJETO", stamp.Project, textWidth, ValueHeight, upperBottom);

            // Responsável técnico (nome e CREA) e o espaço da assinatura, com a linha para assinar
            double rightWidth = block.MaxX - x2 - 2 * Pad;
            paper.Texts.Add(Label(x2, top, "RESPONSÁVEL TÉCNICO"));
            if (stamp.Engineer.Length > 0)
                paper.Texts.Add(Text(stamp.Engineer, x2 + Pad, top - ValueTop, Fit(stamp.Engineer, ValueHeight, rightWidth, 4, 1, MinHeight), 0, PaperAnchor.TopLeft));
            if (stamp.Crea.Length > 0)
                paper.Texts.Add(Text("CREA " + stamp.Crea, x2 + Pad, top - ValueTop - 4.2, 2.0, 0, PaperAnchor.TopLeft));
            paper.Texts.Add(Label(x2, middle, "ASSINATURA"));
            paper.Lines.Add(new PaperLine(x2 + 4, upperBottom + 2.5, block.MaxX - 4, upperBottom + 2.5));

            // Data, escala, formato, folha e ART
            string[] labels = { "DATA", "ESCALA", "FORMATO", "FOLHA", "ART" };
            string[] values = { stamp.Date, "1:" + plan.Scale.ToString(CultureInfo.InvariantCulture), plan.Format.Name, Sheet(number, plan.Tiles.Count), stamp.Art };
            double x = block.MinX;
            for (int i = 0; i < BottomCells.Length; i++)
            {
                if (i > 0) paper.Lines.Add(new PaperLine(x, block.MinY, x, upperBottom));
                paper.Texts.Add(Label(x, upperBottom, labels[i]));
                if (values[i].Length > 0)
                    paper.Texts.Add(Text(values[i], x + Pad, upperBottom - ValueTop, Fit(values[i], ValueHeight, BottomCells[i] - 2 * Pad, 4, 1, MinHeight), 0, PaperAnchor.TopLeft));
                x += BottomCells[i];
            }
        }

        /// <summary>Título do campo no alto do quadro e o valor logo abaixo (quebra em até duas linhas, diminuindo a letra se precisar).</summary>
        private static void Field(SheetPaper paper, double x, double top, string label, string value, double width, double height, double bottom)
        {
            paper.Texts.Add(Label(x, top, label));
            if (value.Length == 0) return;
            double room = top - bottom - ValueTop - 0.5;
            paper.Texts.Add(Text(value, x + Pad, top - ValueTop, Fit(value, height, width, room, 2, MinHeight), width, PaperAnchor.TopLeft));
        }

        private static PaperText Label(double x, double top, string text) =>
            Text(text, x + Pad, top - Pad, LabelHeight, 0, PaperAnchor.TopLeft);

        private static PaperText Text(string text, double x, double y, double height, double width, PaperAnchor anchor) =>
            new PaperText { Text = text, X = x, Y = y, Height = height, Width = width, Anchor = anchor };

        // Espaço entre linhas do MText (5/3 da altura da letra), o que a letra ocupa abaixo da linha (descendentes) e
        // largura média de uma letra (fonte do estilo Standard), em alturas de letra
        private const double LineSpacing = 5.0 / 3.0;
        private const double Descent = 0.35;
        private const double CharWidth = 0.62;

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

        // ---------- Dobras (NBR 13142) e marcas de corte (NBR 16752) ----------

        /// <summary>
        /// Dobras da NBR 13142:1999 para a cópia dobrada ficar no formato A4 com o carimbo à vista, em mm a partir do canto de
        /// baixo à esquerda. As verticais são a partir da direita: a primeira deixa 185 mm (o carimbo) e as outras são as das
        /// figuras 1 a 4 da norma; a sobra à esquerda (210 mm no A0) tem a margem de 25 mm para furar. As horizontais sobem
        /// de 297 em 297 mm. A0, A1 e A2 também têm o canto superior esquerdo dobrado para trás (de 105 mm no alto até a
        /// dobra de 297 mm), para furar só a margem. A folha em pé segue as mesmas medidas pela largura dela.
        /// </summary>
        public static (double[] Vertical, double[] Horizontal, bool Corner) Folds(double width, double height)
        {
            if (Math.Max(width, height) <= 297.5) return (new double[0], new double[0], false); // A4 já é o formato final

            double[] panels = Math.Round(width) switch
            {
                1189.0 => new[] { 185, 185, 185, 185, 119.5, 119.5 },
                841.0 => new[] { 185, 185, 130, 130.0 },
                594.0 => new[] { 185, 96, 96, 96.0 },
                420.0 => new[] { 185, 105.0 },
                297.0 => new[] { 185.0 },
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

        /// <summary>Marcas curtas na borda do papel, fora da moldura, onde a cópia é dobrada.</summary>
        private static void FoldMarks(SheetPaper paper, double width, double height)
        {
            var (vertical, horizontal, corner) = Folds(width, height);
            foreach (double x in vertical)
            {
                paper.Lines.Add(new PaperLine(x, 0, x, FoldMark));
                paper.Lines.Add(new PaperLine(x, height - FoldMark, x, height));
            }
            foreach (double y in horizontal)
            {
                paper.Lines.Add(new PaperLine(0, y, FoldMark, y));
                paper.Lines.Add(new PaperLine(width - FoldMark, y, width, y));
            }
            if (!corner) return;

            // Dobra do canto: de 105 mm no alto até a borda esquerda na altura de 297 mm; só as duas pontas, nas margens
            double dx = 0 - 105, dy = 297 - height, length = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length * CornerFoldMark, uy = dy / length * CornerFoldMark;
            paper.Lines.Add(new PaperLine(105, height, 105 + ux, height + uy));
            paper.Lines.Add(new PaperLine(0, 297, -ux, 297 - uy));
        }

        /// <summary>Marcas de corte nos quatro cantos do papel: um L cheio de 10 x 5 mm (NBR 16752, figuras 22 e 23).</summary>
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
