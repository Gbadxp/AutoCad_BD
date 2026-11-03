
namespace FiberPlugin.Core
{
    /// <summary>Retângulo simples em coordenadas do desenho (metros) ou do papel (mm).</summary>
    public struct Rect
    {
        public Rect(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double CenterX => (MinX + MaxX) / 2.0;
        public double CenterY => (MinY + MaxY) / 2.0;

        public bool Intersects(Rect other)
        {
            return MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;
        }
    }

    /// <summary>Folha da série A, sempre com as medidas em paisagem (largura maior que altura), em mm.</summary>
    public class SheetFormat
    {
        public static readonly SheetFormat[] All =
        {
            new SheetFormat("A0", 1189, 841),
            new SheetFormat("A1", 841, 594),
            new SheetFormat("A2", 594, 420),
            new SheetFormat("A3", 420, 297),
            new SheetFormat("A4", 297, 210)
        };

        // Margens da moldura (NBR 16752, tabela 4: 20 mm à esquerda para furar e arquivar, 10 mm nas demais)
        public const double MarginLeft = 20;
        public const double Margin = 10;

        // Carimbo (SheetFrame) deitado no canto inferior direito do quadro, numa faixa embaixo: a legenda, com os 180 mm de
        // comprimento que a NBR 16752 (5.3) pede em todos os formatos (sete linhas de 7 mm), e à esquerda dela a articulação
        // das folhas (até 120 mm). Os 180 mm mais a margem de 10 mm são o painel da frente da cópia dobrada em A4 (190 mm,
        // figura 21). Sem espaço ao lado (A4 em pé), a articulação vai em cima da legenda. O norte e os símbolos ficam na coluna
        // estreita da direita, acima da faixa
        public const double TitleBlockWidth = 180;
        public const double LegendRow = 7;   // Altura de uma linha da legenda
        public const double LegendHeight = 7 * LegendRow;
        private const double ArticulationMaxWidth = 120, ArticulationMinWidth = 60;
        private const double ArticulationOnTopHeight = 5 * LegendRow;

        public SheetFormat(string name, double width, double height)
        {
            Name = name;
            LongSide = width;
            ShortSide = height;
        }

        public string Name { get; }
        public double LongSide { get; }
        public double ShortSide { get; }

        public static SheetFormat? Find(string name) =>
            All.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public double PaperWidth(bool landscape) => landscape ? LongSide : ShortSide;
        public double PaperHeight(bool landscape) => landscape ? ShortSide : LongSide;

        /// <summary>Moldura (o quadro), em mm no papel.</summary>
        public Rect Frame(bool landscape) =>
            new Rect(MarginLeft, Margin, PaperWidth(landscape) - Margin, PaperHeight(landscape) - Margin);

        /// <summary>Largura da linha do quadro (NBR 16752, tabela 4): 1,0 mm no A0 e no A1, 0,7 mm nos menores.</summary>
        public double FrameLineWidth => LongSide >= 840 ? 1.0 : 0.7;

        /// <summary>Campos da malha de referência no lado maior e no menor (NBR 16752, tabela 5).</summary>
        public (int Long, int Short) GridFields => Name switch
        {
            "A0" => (24, 16),
            "A1" => (16, 12),
            "A2" => (12, 8),
            "A3" => (8, 6),
            _ => (6, 4)
        };

        /// <summary>Largura da coluna de norte e símbolos, à direita do viewport (45 a 65 mm, conforme a folha).</summary>
        public double LegendWidth(bool landscape) => Math.Max(45, Math.Min(65, Math.Round(PaperWidth(landscape) * 0.14)));

        /// <summary>Largura da articulação das folhas, à esquerda da legenda; 0 = não cabe ao lado (vai em cima da legenda).</summary>
        public double ArticulationWidth(bool landscape)
        {
            double room = Frame(landscape).Width - TitleBlockWidth;
            return room >= ArticulationMinWidth ? Math.Min(ArticulationMaxWidth, room) : 0;
        }

        /// <summary>Altura da faixa do carimbo: a da legenda, ou a da legenda mais a articulação em cima dela.</summary>
        public double TitleBlockHeight(bool landscape) => LegendHeight + (ArticulationWidth(landscape) > 0 ? 0 : ArticulationOnTopHeight);

        /// <summary>Área do viewport: o quadro menos a faixa do carimbo e a coluna da direita, em mm no papel.</summary>
        public Rect ViewportArea(bool landscape)
        {
            Rect frame = Frame(landscape);
            return new Rect(frame.MinX, frame.MinY + TitleBlockHeight(landscape), frame.MaxX - LegendWidth(landscape), frame.MaxY);
        }

        /// <summary>Norte e símbolos, na coluna da direita (acima da faixa do carimbo), em mm no papel.</summary>
        public Rect LegendArea(bool landscape)
        {
            Rect frame = Frame(landscape);
            return new Rect(frame.MaxX - LegendWidth(landscape), frame.MinY + TitleBlockHeight(landscape), frame.MaxX, frame.MaxY);
        }

        /// <summary>Legenda (180 mm), no canto de baixo à direita do quadro (em A4 em pé ocupa a largura toda), em mm no papel.</summary>
        public Rect TitleBlock(bool landscape)
        {
            Rect frame = Frame(landscape);
            return new Rect(Math.Max(frame.MinX, frame.MaxX - TitleBlockWidth), frame.MinY, frame.MaxX, frame.MinY + LegendHeight);
        }

        /// <summary>Articulação das folhas: à esquerda da legenda ou, sem espaço, em cima dela, em mm no papel.</summary>
        public Rect ArticulationArea(bool landscape)
        {
            Rect legend = TitleBlock(landscape);
            double width = ArticulationWidth(landscape);
            return width > 0
                ? new Rect(legend.MinX - width, legend.MinY, legend.MinX, legend.MaxY)
                : new Rect(legend.MinX, legend.MaxY, legend.MaxX, legend.MaxY + ArticulationOnTopHeight);
        }
    }

    /// <summary>Um pedaço da área, que vira uma folha (layout com viewport).</summary>
    public class SheetTile
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public Rect ModelArea { get; set; }   // Trecho do desenho mostrado no viewport (metros)
    }

    public class SheetPlan
    {
        public SheetFormat Format { get; set; } = SheetFormat.All[2];
        public bool Landscape { get; set; }
        public int Scale { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public List<SheetTile> Tiles { get; } = new List<SheetTile>();

        /// <summary>Pedaços da grade sem nenhum elemento do desenho: não viram folha.</summary>
        public List<SheetTile> Skipped { get; } = new List<SheetTile>();

        public int SkippedEmpty => Skipped.Count;

        /// <summary>Metros de desenho por mm de papel (1:1000 → 1 m por mm).</summary>
        public double MetersPerMm => Scale / 1000.0;
    }

    /// <summary>
    /// Divide uma área do desenho em folhas. Cada folha cobre a área útil do viewport na escala pedida,
    /// com sobreposição entre folhas vizinhas. A grade é centralizada na área, e folhas sem nenhum
    /// elemento do desenho são descartadas.
    /// </summary>
    public static class SheetPlanner
    {
        /// <param name="area">Área escolhida pelo usuário (metros).</param>
        /// <param name="overlap">Sobreposição entre folhas vizinhas (0,05 = 5%).</param>
        /// <param name="content">Extensões dos elementos do desenho, para descartar folhas vazias.</param>
        public static SheetPlan Plan(Rect area, SheetFormat format, bool landscape, int scale, double overlap, IReadOnlyList<Rect> content)
        {
            var plan = new SheetPlan { Format = format, Landscape = landscape, Scale = scale };

            Rect vp = format.ViewportArea(landscape);
            double coverW = vp.Width * plan.MetersPerMm;
            double coverH = vp.Height * plan.MetersPerMm;
            double stepX = coverW * (1 - overlap);
            double stepY = coverH * (1 - overlap);

            plan.Columns = Count(area.Width, coverW, stepX);
            plan.Rows = Count(area.Height, coverH, stepY);

            // Centraliza a grade na área escolhida
            double totalW = coverW + (plan.Columns - 1) * stepX;
            double totalH = coverH + (plan.Rows - 1) * stepY;
            double startX = area.MinX - (totalW - area.Width) / 2.0;
            double startTop = area.MaxY + (totalH - area.Height) / 2.0;

            // Ordem de leitura: de cima para baixo, da esquerda para a direita
            for (int row = 0; row < plan.Rows; row++)
            {
                for (int col = 0; col < plan.Columns; col++)
                {
                    double minX = startX + col * stepX;
                    double maxY = startTop - row * stepY;
                    var tileArea = new Rect(minX, maxY - coverH, minX + coverW, maxY);

                    // Só o que está dentro da área escolhida conta como conteúdo da folha
                    bool hasContent = content.Any(c => c.Intersects(tileArea) && c.Intersects(area));
                    var tile = new SheetTile { Row = row, Column = col, ModelArea = tileArea };
                    (hasContent ? plan.Tiles : plan.Skipped).Add(tile);
                }
            }

            return plan;
        }

        /// <summary>Planeja em paisagem e em retrato e fica com o que gerar menos folhas (empate: paisagem).</summary>
        public static SheetPlan PlanBestOrientation(Rect area, SheetFormat format, int scale, double overlap, IReadOnlyList<Rect> content)
        {
            SheetPlan landscape = Plan(area, format, true, scale, overlap, content);
            SheetPlan portrait = Plan(area, format, false, scale, overlap, content);
            return portrait.Tiles.Count < landscape.Tiles.Count ? portrait : landscape;
        }

        /// <summary>
        /// Quantos pedaços a grade teria, com os vazios, sem olhar o conteúdo: para recusar logo uma divisão grande demais
        /// (área enorme numa escala pequena) antes de planejar.
        /// </summary>
        public static long GridCells(Rect area, SheetFormat format, bool landscape, int scale, double overlap)
        {
            Rect vp = format.ViewportArea(landscape);
            double coverW = vp.Width * scale / 1000.0, coverH = vp.Height * scale / 1000.0;
            return (long)Count(area.Width, coverW, coverW * (1 - overlap)) * Count(area.Height, coverH, coverH * (1 - overlap));
        }

        private static int Count(double length, double cover, double step)
        {
            if (length <= cover) return 1;
            return (int)Math.Ceiling((length - cover) / step - 1e-9) + 1;
        }
    }
}
