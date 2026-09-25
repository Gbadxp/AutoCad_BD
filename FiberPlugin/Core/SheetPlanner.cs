using System;
using System.Collections.Generic;
using System.Linq;

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

        // Margens da moldura (NBR 10068: 25 mm à esquerda para encadernação, 10 mm nas demais)
        public const double MarginLeft = 25;
        public const double Margin = 10;

        // Faixa na base da moldura com número da folha, escala e formato
        public const double InfoBandHeight = 12;

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

        /// <summary>Moldura, em mm no papel.</summary>
        public Rect Frame(bool landscape) =>
            new Rect(MarginLeft, Margin, PaperWidth(landscape) - Margin, PaperHeight(landscape) - Margin);

        /// <summary>Área do viewport: a moldura menos a faixa de informações, em mm no papel.</summary>
        public Rect ViewportArea(bool landscape)
        {
            Rect frame = Frame(landscape);
            return new Rect(frame.MinX, frame.MinY + InfoBandHeight, frame.MaxX, frame.MaxY);
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
        public int SkippedEmpty { get; set; }

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
        public static SheetPlan Plan(Rect area, SheetFormat format, bool landscape, int scale, double overlap, IList<Rect> content)
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
                    if (!hasContent)
                    {
                        plan.SkippedEmpty++;
                        continue;
                    }

                    plan.Tiles.Add(new SheetTile { Row = row, Column = col, ModelArea = tileArea });
                }
            }

            return plan;
        }

        /// <summary>Planeja em paisagem e em retrato e fica com o que gerar menos folhas (empate: paisagem).</summary>
        public static SheetPlan PlanBestOrientation(Rect area, SheetFormat format, int scale, double overlap, IList<Rect> content)
        {
            SheetPlan landscape = Plan(area, format, true, scale, overlap, content);
            SheetPlan portrait = Plan(area, format, false, scale, overlap, content);
            return portrait.Tiles.Count < landscape.Tiles.Count ? portrait : landscape;
        }

        private static int Count(double length, double cover, double step)
        {
            if (length <= cover) return 1;
            return (int)Math.Ceiling((length - cover) / step - 1e-9) + 1;
        }
    }
}
