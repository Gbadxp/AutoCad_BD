
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

        // Faixa do carimbo (SheetFrame), embaixo do quadro na largura toda: no canto de baixo à direita a legenda, com os 180 mm
        // de comprimento que a NBR 16752 (5.3) pede em todos os formatos (sete linhas de 7 mm); colado nela o quadro do norte e
        // dos símbolos; à esquerda, a articulação das folhas (até 120 mm). Os 180 mm mais a margem de 10 mm são o painel da
        // frente da cópia dobrada em A4 (190 mm, figura 21). O que não cabe ao lado da legenda (A4 e A3 em pé) vai numa faixa
        // em cima dela
        public const double TitleBlockWidth = 180;
        public const double LegendRow = 7;   // Altura de uma linha da legenda
        public const double LegendHeight = 7 * LegendRow;
        private const double ArticulationMaxWidth = 120, ArticulationMinWidth = 60;

        // Quadro dos símbolos: o norte numa coluna de 24 mm e, ao lado, colunas de 38 mm com linhas de 6 mm embaixo do título.
        // A altura da faixa é a que cabe a legenda mais cheia (14 blocos, cabo, backbone e esforço: SheetLegend), para a divisão
        // das folhas não mudar com os símbolos do desenho; a largura do quadro é a dos símbolos que o desenho tem
        public const int MaxSymbols = 17;
        public const double NorthWidth = 24, SymbolColumn = 38, SymbolRow = 6, SymbolHeader = 6;

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

        /// <summary>Altura da faixa do carimbo (a mesma com qualquer quantidade de símbolos).</summary>
        public double TitleBlockHeight(bool landscape) => Band(landscape, MaxSymbols).Height;

        /// <summary>Área do viewport: o quadro menos a faixa do carimbo, em mm no papel.</summary>
        public Rect ViewportArea(bool landscape)
        {
            Rect frame = Frame(landscape);
            return new Rect(frame.MinX, frame.MinY + TitleBlockHeight(landscape), frame.MaxX, frame.MaxY);
        }

        /// <summary>Legenda (180 mm), no canto de baixo à direita do quadro (em A4 em pé ocupa a largura toda), em mm no papel.</summary>
        public Rect TitleBlock(bool landscape)
        {
            Rect frame = Frame(landscape);
            return new Rect(Math.Max(frame.MinX, frame.MaxX - TitleBlockWidth), frame.MinY, frame.MaxX, frame.MinY + LegendHeight);
        }

        /// <summary>
        /// Onde vai cada parte da faixa do carimbo, em mm no papel:
        /// <list type="bullet">
        /// <item>cabendo, tudo ao lado da legenda: articulação, norte e símbolos, legenda;</item>
        /// <item>senão, a articulação ao lado da legenda e, numa faixa em cima, o norte e os símbolos à direita (A4 deitado e A3 em pé);</item>
        /// <item>sem espaço nem para a articulação ao lado (A4 em pé), só a legenda embaixo e, em cima, articulação, norte e símbolos.</item>
        /// </list>
        /// </summary>
        /// <param name="symbols">Quantos símbolos o desenho tem (a largura do quadro dos símbolos).</param>
        public SheetBand Band(bool landscape, int symbols)
        {
            Rect frame = Frame(landscape);
            Rect block = TitleBlock(landscape);
            int count = Math.Max(0, Math.Min(MaxSymbols, symbols));
            double room = block.MinX - frame.MinX, top = block.MaxY;

            if (room >= SymbolsWidth(LegendHeight, MaxSymbols) + ArticulationMinWidth)
            {
                var symbolBox = new Rect(block.MinX - SymbolsWidth(LegendHeight, count), frame.MinY, block.MinX, top);
                var articulation = new Rect(symbolBox.MinX - Math.Min(ArticulationMaxWidth, symbolBox.MinX - frame.MinX), frame.MinY, symbolBox.MinX, top);
                return new SheetBand(block, articulation, symbolBox, new Rect(frame.MinX, frame.MinY, articulation.MinX, top), LegendHeight);
            }

            // Faixa de cima, na largura do quadro: a menor altura (de 7 em 7 mm) em que cabem os símbolos (e a articulação, se
            // ela também for para cima)
            bool beside = room >= ArticulationMinWidth;
            double upper = 3 * LegendRow;
            while (SymbolsWidth(upper, MaxSymbols) + (beside ? 0 : ArticulationMinWidth) > frame.Width) upper += LegendRow;
            var symbols2 = new Rect(frame.MaxX - SymbolsWidth(upper, count), top, frame.MaxX, top + upper);
            if (beside)
            {
                var articulation = new Rect(block.MinX - Math.Min(ArticulationMaxWidth, room), frame.MinY, block.MinX, top);
                return new SheetBand(block, articulation, symbols2, new Rect(frame.MinX, top, symbols2.MinX, top + upper), LegendHeight + upper);
            }
            var above = new Rect(symbols2.MinX - Math.Min(ArticulationMaxWidth, symbols2.MinX - frame.MinX), top, symbols2.MinX, top + upper);
            return new SheetBand(block, above, symbols2, new Rect(frame.MinX, top, above.MinX, top + upper), LegendHeight + upper);
        }

        /// <summary>Linhas de símbolos que cabem num quadro dessa altura, embaixo do título.</summary>
        public static int SymbolRows(double height) => Math.Max(1, (int)Math.Floor((height - SymbolHeader - 1) / SymbolRow));

        /// <summary>Largura do quadro do norte e dos símbolos: o norte e as colunas que os símbolos precisam nessa altura.</summary>
        public static double SymbolsWidth(double height, int symbols) =>
            NorthWidth + (symbols > 0 ? (int)Math.Ceiling(symbols / (double)SymbolRows(height)) * SymbolColumn : 0);
    }

    /// <summary>As partes da faixa do carimbo (SheetFormat.Band), em mm no papel.</summary>
    public readonly struct SheetBand
    {
        public SheetBand(Rect titleBlock, Rect articulation, Rect symbols, Rect free, double height)
        {
            TitleBlock = titleBlock; Articulation = articulation; Symbols = symbols; Free = free; Height = height;
        }

        public Rect TitleBlock { get; }    // A legenda de 180 mm
        public Rect Articulation { get; }  // Articulação das folhas
        public Rect Symbols { get; }       // Norte e símbolos
        public Rect Free { get; }          // A sobra à esquerda (para o nome do desenho); largura 0 = sem sobra
        public double Height { get; }      // Da linha de baixo do quadro até o alto da faixa
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

        /// <summary>Distância entre os centros de folhas vizinhas (metros): o tamanho de cada folha sem a sobreposição.</summary>
        public double StepX { get; set; }
        public double StepY { get; set; }

        public List<SheetTile> Tiles { get; } = new List<SheetTile>();

        /// <summary>Pedaços da grade sem nenhum elemento do desenho: não viram folha.</summary>
        public List<SheetTile> Skipped { get; } = new List<SheetTile>();

        public int SkippedEmpty => Skipped.Count;

        /// <summary>Metros de desenho por mm de papel (1:1000 → 1 m por mm).</summary>
        public double MetersPerMm => Scale / 1000.0;
    }

    /// <summary>
    /// Divide uma área do desenho em folhas. Cada folha cobre a área útil do viewport na escala pedida,
    /// com sobreposição entre folhas vizinhas. A grade cobre o desenho que está dentro da área, centralizada nele, e folhas
    /// sem nenhum elemento do desenho são descartadas.
    /// </summary>
    public static class SheetPlanner
    {
        /// <param name="area">Área escolhida pelo usuário (metros).</param>
        /// <param name="overlap">Sobreposição entre folhas vizinhas (0,05 = 5%).</param>
        /// <param name="content">Extensões dos elementos do desenho, para centralizar a grade e descartar folhas vazias.</param>
        public static SheetPlan Plan(Rect area, SheetFormat format, bool landscape, int scale, double overlap, IReadOnlyList<Rect> content)
        {
            var plan = new SheetPlan { Format = format, Landscape = landscape, Scale = scale };

            Rect vp = format.ViewportArea(landscape);
            double coverW = vp.Width * plan.MetersPerMm;
            double coverH = vp.Height * plan.MetersPerMm;
            double stepX = coverW * (1 - overlap);
            double stepY = coverH * (1 - overlap);
            plan.StepX = stepX;
            plan.StepY = stepY;

            // A grade cobre o desenho que está dentro da área (a área inteira, se ela não tiver nada), centralizada nele: uma
            // área marcada com folga de um lado não deixa o desenho encostado num canto das folhas
            Rect drawn = Drawn(area, content) ?? area;
            plan.Columns = Count(drawn.Width, coverW, stepX);
            plan.Rows = Count(drawn.Height, coverH, stepY);

            double totalW = coverW + (plan.Columns - 1) * stepX;
            double totalH = coverH + (plan.Rows - 1) * stepY;
            double startX = drawn.MinX - (totalW - drawn.Width) / 2.0;
            double startTop = drawn.MaxY + (totalH - drawn.Height) / 2.0;

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

        /// <summary>Extensão do que o desenho tem dentro da área (cada elemento cortado na área); null se não tiver nada.</summary>
        private static Rect? Drawn(Rect area, IReadOnlyList<Rect> content)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Rect c in content)
            {
                if (!c.Intersects(area)) continue;
                minX = Math.Min(minX, Math.Max(c.MinX, area.MinX));
                minY = Math.Min(minY, Math.Max(c.MinY, area.MinY));
                maxX = Math.Max(maxX, Math.Min(c.MaxX, area.MaxX));
                maxY = Math.Max(maxY, Math.Min(c.MaxY, area.MaxY));
            }
            return minX <= maxX && minY <= maxY ? new Rect(minX, minY, maxX, maxY) : (Rect?)null;
        }

        private static int Count(double length, double cover, double step)
        {
            if (length <= cover) return 1;
            return (int)Math.Ceiling((length - cover) / step - 1e-9) + 1;
        }
    }
}
