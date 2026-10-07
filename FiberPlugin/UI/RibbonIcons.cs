using System.IO;
using M = System.Windows.Media;
using MI = System.Windows.Media.Imaging;
using W = System.Windows;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Ícones coloridos no estilo dos ícones da faixa de opções do AutoCAD: objetos em cinza claro,
    /// detalhes em azul, amarelo para pastas e réguas, "+" verde nos comandos de inserir, grips azuis.
    /// Desenhados por código numa grade de 32 x 32, então não dependem de arquivos de imagem.
    /// </summary>
    internal static class RibbonIcons
    {
        private delegate void Painter(M.DrawingContext dc, Ink ink);

        /// <summary>Cores do ícone. O contorno acompanha o tema (claro/escuro) do AutoCAD.</summary>
        private sealed class Ink
        {
            public Ink(bool dark)
            {
                Line = Brush(dark ? "#D9D9D9" : "#595959");
                Paper = Brush(dark ? "#E6E9EE" : "#FFFFFF");
                Blue = Brush(dark ? "#3AA0DD" : "#1B7FC4");
                Green = Brush(dark ? "#6CC04A" : "#3E9E2E");
                Yellow = Brush("#F5C242");
                YellowDark = Brush("#C8901A");
                Red = Brush(dark ? "#EE5A43" : "#D23B24");
                White = Brush("#FFFFFF");
                Fiber = Brush("#1FB6CC");
            }

            public M.Brush Line { get; }
            public M.Brush Paper { get; }
            public M.Brush Blue { get; }
            public M.Brush Green { get; }
            public M.Brush Yellow { get; }
            public M.Brush YellowDark { get; }
            public M.Brush Red { get; }
            public M.Brush White { get; }
            public M.Brush Fiber { get; }

            public static M.Pen Pen(M.Brush brush, double width)
            {
                var pen = new M.Pen(brush, width)
                {
                    StartLineCap = M.PenLineCap.Round,
                    EndLineCap = M.PenLineCap.Round,
                    LineJoin = M.PenLineJoin.Round
                };
                pen.Freeze();
                return pen;
            }

            private static M.Brush Brush(string hex)
            {
                var brush = new M.SolidColorBrush((M.Color)M.ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
        }

        private static readonly Dictionary<string, Painter> Painters = new Dictionary<string, Painter>(StringComparer.OrdinalIgnoreCase)
        {
            ["FIBRA"] = PaintMenu,
            ["FIBRA_LANCAR_CABO"] = PaintCable,
            ["FIBRA_ROTEAMENTO_AUTO"] = PaintRoute,
            ["FIBRA_INSERIR_POSTE"] = PaintPole,
            ["FIBRA_INSERIR_CTO"] = PaintCto,
            ["FIBRA_INSERIR_CEO"] = PaintCeo,
            ["FIBRA_INSERIR_ELETRICOS"] = PaintElectrical,
            ["FIBRA_INSERIR_AMARRACAO"] = PaintAnchoring,
            ["FIBRA_RENUMERAR"] = PaintRenumber,
            ["FIBRA_NUMERAR_AUTO"] = PaintAutoNumber,
            ["FIBRA_TAMANHO_BLOCO"] = PaintBlockSize,
            ["FIBRA_ESFORCO_TOTAL"] = PaintEffortPole,
            ["FIBRA_ESFORCO_PERCURSO"] = PaintEffortRoute,
            ["FIBRA_PARAMETROS"] = PaintParameters,
            ["FIBRA_ESFORCO_EXISTENTE"] = PaintExistingEffort,
            ["FIBRA_RELATORIO"] = PaintReport,
            ["FIBRA_VERIFICAR"] = PaintCheck,
            ["FIBRA_MEMORIAL"] = PaintMemorial,
            ["FIBRA_MEMORIAL_ESFORCO"] = PaintEffortMemorial,
            ["FIBRA_COORDENADAS_POSTES"] = PaintPoleCoordinates,
            ["FIBRA_NORMA"] = PaintNorm,
            ["FIBRA_CONFIGURACOES"] = PaintSettings,
            ["FIBRA_GERAR_FOLHAS"] = PaintSheets,
            ["FIBRA_IMPORTAR_KML"] = PaintKmlImport,
            ["FIBRA_EXPORTAR_KML"] = PaintKmlExport,
            ["FIBRA_IMPORTAR_RUAS"] = PaintRoads
        };

        private static readonly Dictionary<string, object> Cache = new Dictionary<string, object>();

        /// <summary>Desenho do comando; comandos sem desenho próprio usam o de blocos.</summary>
        private static Painter PainterFor(string command) =>
            Painters.TryGetValue(command, out Painter? painter) ? painter : PaintBlocks;

        /// <summary>Ícone para a faixa de opções (tamanho lógico 16 ou 32, desenhado em 2x para telas de alta resolução).</summary>
        public static M.ImageSource Get(string command, int size)
        {
            Painter painter = PainterFor(command);
            string key = $"wpf|{command}|{size}|{Theme.Current.IsDark}";
            if (Cache.TryGetValue(key, out object? cached)) return (M.ImageSource)cached;

            MI.BitmapSource image = Render(painter, size, 2.0);
            Cache[key] = image;
            return image;
        }

        /// <summary>Mesmo ícone como Bitmap do Windows Forms, no tamanho exato em pixels (cartões do menu).</summary>
        public static System.Drawing.Bitmap GetBitmap(string command, int pixels)
        {
            Painter painter = PainterFor(command);
            pixels = Math.Max(1, pixels);
            string key = $"gdi|{command}|{pixels}|{Theme.Current.IsDark}";
            if (Cache.TryGetValue(key, out object? cached)) return (System.Drawing.Bitmap)cached;

            MI.BitmapSource image = Render(painter, pixels, 1.0);
            var encoder = new MI.PngBitmapEncoder();
            encoder.Frames.Add(MI.BitmapFrame.Create(image));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                // O Bitmap precisa do stream aberto: copia para um bitmap independente
                using (var temp = new System.Drawing.Bitmap(stream))
                {
                    var bitmap = new System.Drawing.Bitmap(temp);
                    Cache[key] = bitmap;
                    return bitmap;
                }
            }
        }

        private static MI.BitmapSource Render(Painter painter, int size, double resolution)
        {
            int pixels = (int)Math.Round(size * resolution);
            var visual = new M.DrawingVisual();
            using (M.DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new M.ScaleTransform(size / 32.0, size / 32.0));
                painter(dc, new Ink(Theme.Current.IsDark));
                dc.Pop();
            }

            var bitmap = new MI.RenderTargetBitmap(pixels, pixels, 96 * resolution, 96 * resolution, M.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ---------- Auxiliares de desenho (coordenadas na grade 32 x 32) ----------

        private static M.Geometry G(string path) => M.Geometry.Parse(path);

        private static void Plus(M.DrawingContext dc, Ink ink, double cx, double cy)
        {
            // Invariant: com a cultura pt-BR os decimais sairiam com vírgula e quebrariam o caminho
            dc.DrawGeometry(ink.Green, null, G(FormattableString.Invariant(
                $"M{cx - 1.8},{cy - 5.5} h3.6 v3.7 h3.7 v3.6 h-3.7 v3.7 h-3.6 v-3.7 h-3.7 v-3.6 h3.7 Z")));
        }

        private static void Grip(M.DrawingContext dc, Ink ink, double x, double y)
        {
            dc.DrawRectangle(ink.Blue, null, new W.Rect(x - 2.2, y - 2.2, 4.4, 4.4));
        }

        private static void Arrow(M.DrawingContext dc, M.Brush brush, double x1, double y1, double x2, double y2, double width)
        {
            double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / len, uy = dy / len;
            double head = width * 3.0;
            double bx = x2 - ux * head, by = y2 - uy * head;

            dc.DrawLine(Ink.Pen(brush, width), new W.Point(x1, y1), new W.Point(bx, by));
            var tip = new M.StreamGeometry();
            using (M.StreamGeometryContext g = tip.Open())
            {
                g.BeginFigure(new W.Point(x2, y2), true, true);
                g.LineTo(new W.Point(bx - uy * head * 0.75, by + ux * head * 0.75), true, false);
                g.LineTo(new W.Point(bx + uy * head * 0.75, by - ux * head * 0.75), true, false);
            }
            tip.Freeze();
            dc.DrawGeometry(brush, null, tip);
        }

        // ---------- Ícones ----------

        private static void PaintMenu(M.DrawingContext dc, Ink ink)
        {
            dc.DrawRoundedRectangle(ink.Blue, null, new W.Rect(3, 3, 26, 26), 5, 5);
            M.Pen fiber = Ink.Pen(ink.White, 2.2);
            dc.DrawGeometry(null, fiber, G("M8,23 C14,23 13,10 24,10"));
            dc.DrawGeometry(null, fiber, G("M8,16 C14,16 16,21 24,21"));
            dc.DrawEllipse(ink.Yellow, null, new W.Point(24, 10), 2.4, 2.4);
            dc.DrawEllipse(ink.Yellow, null, new W.Point(24, 21), 2.4, 2.4);
        }

        private static void PaintCable(M.DrawingContext dc, Ink ink)
        {
            dc.DrawGeometry(null, Ink.Pen(ink.Green, 2.6), G("M4,26 L12,11 L20,20 L28,6"));
            Grip(dc, ink, 4, 26);
            Grip(dc, ink, 12, 11);
            Grip(dc, ink, 20, 20);
            Grip(dc, ink, 28, 6);
        }

        private static void PaintRoute(M.DrawingContext dc, Ink ink)
        {
            dc.DrawGeometry(null, Ink.Pen(ink.Green, 2.4), G("M5,25 L14,12 L25,22"));
            foreach (var (x, y) in new[] { (5.0, 25.0), (14.0, 12.0), (25.0, 22.0) })
            {
                dc.DrawEllipse(ink.Paper, Ink.Pen(ink.Line, 1.4), new W.Point(x, y), 3.3, 3.3);
            }
            // Faísca de "automático"
            dc.DrawGeometry(ink.Yellow, Ink.Pen(ink.YellowDark, 0.8), G("M25,1.5 L26.5,6 L31,7.5 L26.5,9 L25,13.5 L23.5,9 L19,7.5 L23.5,6 Z"));
        }

        private static void PaintPole(M.DrawingContext dc, Ink ink)
        {
            dc.DrawRectangle(ink.Line, null, new W.Rect(12, 5, 4, 24));        // Poste
            dc.DrawRectangle(ink.Line, null, new W.Rect(5, 9, 18, 3));         // Cruzeta
            dc.DrawEllipse(ink.Blue, null, new W.Point(6.5, 7), 2, 2);          // Isoladores
            dc.DrawEllipse(ink.Blue, null, new W.Point(21.5, 7), 2, 2);
            Plus(dc, ink, 25.5, 24.5);
        }

        private static void PaintBlocks(M.DrawingContext dc, Ink ink)
        {
            dc.DrawRectangle(null, Ink.Pen(ink.Line, 1.6), new W.Rect(4, 4, 14, 14));
            dc.DrawRectangle(ink.Blue, Ink.Pen(ink.Paper, 1.2), new W.Rect(10, 10, 14, 14));
            Plus(dc, ink, 25.5, 24.5);
        }

        private static void PaintCto(M.DrawingContext dc, Ink ink)
        {
            // Caixa de terminação com as saídas de drop
            dc.DrawRoundedRectangle(ink.Paper, Ink.Pen(ink.Line, 1.4), new W.Rect(5, 4, 16, 20), 2, 2);
            M.Pen port = Ink.Pen(ink.Fiber, 1.6);
            for (int i = 0; i < 4; i++) dc.DrawLine(port, new W.Point(8, 8 + i * 4), new W.Point(18, 8 + i * 4));
            Plus(dc, ink, 25.5, 24.5);
        }

        private static void PaintCeo(M.DrawingContext dc, Ink ink)
        {
            // Caixa de emenda (cilíndrica) com fibras entrando dos dois lados
            M.Pen fiber = Ink.Pen(ink.Fiber, 1.8);
            dc.DrawLine(fiber, new W.Point(1, 13), new W.Point(7, 13));
            dc.DrawLine(fiber, new W.Point(19, 13), new W.Point(24, 13));
            dc.DrawRoundedRectangle(ink.Blue, Ink.Pen(ink.Paper, 1.0), new W.Rect(6, 6, 14, 14), 7, 7);
            dc.DrawLine(Ink.Pen(ink.White, 1.4), new W.Point(9, 13), new W.Point(17, 13));
            Plus(dc, ink, 25.5, 24.5);
        }

        private static void PaintElectrical(M.DrawingContext dc, Ink ink)
        {
            dc.DrawGeometry(ink.Yellow, Ink.Pen(ink.YellowDark, 0.9), G("M16,2 L6,17 H13 L10,30 L24,12 H16.5 L20,2 Z"));
            Plus(dc, ink, 25.5, 24.5);
        }

        private static void PaintAnchoring(M.DrawingContext dc, Ink ink)
        {
            // Poste, cabo de amarração e âncora no chão
            dc.DrawRectangle(ink.Line, null, new W.Rect(7, 3, 3.5, 25));
            dc.DrawLine(Ink.Pen(ink.Line, 1.4), new W.Point(3, 28.5), new W.Point(29, 28.5));
            dc.DrawLine(Ink.Pen(ink.Blue, 1.8), new W.Point(10, 6), new W.Point(25, 25));
            dc.DrawEllipse(ink.Red, null, new W.Point(25.5, 25.5), 2.6, 2.6);
        }

        private static void PaintRenumber(M.DrawingContext dc, Ink ink)
        {
            // Etiqueta com número e a seta circular de "trocar"
            dc.DrawRoundedRectangle(ink.Paper, Ink.Pen(ink.Line, 1.6), new W.Rect(1.5, 2.5, 22, 17), 2.5, 2.5);
            var text = new M.FormattedText("12", System.Globalization.CultureInfo.InvariantCulture, W.FlowDirection.LeftToRight,
                new M.Typeface(new M.FontFamily("Segoe UI"), W.FontStyles.Normal, W.FontWeights.Bold, W.FontStretches.Normal),
                15, ink.Blue, 1.0);
            dc.DrawText(text, new W.Point(12.5 - text.Width / 2, 11 - text.Height / 2));

            dc.DrawGeometry(null, Ink.Pen(ink.Green, 2.4), G("M27,19 A5.5,5.5 0 1 1 19.8,27"));
            Arrow(dc, ink.Green, 21.5, 28.6, 17.2, 23.6, 2.0);
        }

        private static void PaintAutoNumber(M.DrawingContext dc, Ink ink)
        {
            // Postes ligados pelo cabo e numerados em sequência, a partir do primeiro (azul), com a seta do sentido
            dc.DrawGeometry(null, Ink.Pen(ink.Fiber, 2.6), G("M6.5,24.5 L16,16 L25,9"));
            var font = new M.Typeface(new M.FontFamily("Segoe UI"), W.FontStyles.Normal, W.FontWeights.Bold, W.FontStretches.Normal);
            var centers = new[] { new W.Point(6.5, 24.5), new W.Point(16, 16), new W.Point(25, 9) };
            for (int i = 0; i < centers.Length; i++)
            {
                bool first = i == 0;
                dc.DrawEllipse(first ? ink.Blue : ink.Paper, Ink.Pen(ink.Blue, 1.8), centers[i], 5.6, 5.6);
                var number = new M.FormattedText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture, W.FlowDirection.LeftToRight, font, 9, first ? ink.White : ink.Blue, 1.0);
                dc.DrawText(number, new W.Point(centers[i].X - number.Width / 2, centers[i].Y - number.Height / 2));
            }
            Arrow(dc, ink.Green, 20, 28.5, 30, 28.5, 2.2);
        }

        private static void PaintBlockSize(M.DrawingContext dc, Ink ink)
        {
            // Bloco pequeno, contorno do tamanho novo e seta diagonal de ampliar
            dc.DrawRectangle(null, new M.Pen(ink.Line, 1.4) { DashStyle = M.DashStyles.Dash }, new W.Rect(3, 3, 26, 26));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(3, 17, 12, 12));
            Arrow(dc, ink.Green, 13, 19, 26.5, 5.5, 2.2);
            Grip(dc, ink, 29, 3);
        }

        private static void PaintEffortPole(M.DrawingContext dc, Ink ink)
        {
            dc.DrawEllipse(ink.Paper, Ink.Pen(ink.Line, 1.8), new W.Point(10, 22), 5.5, 5.5);
            dc.DrawEllipse(ink.Line, null, new W.Point(10, 22), 1.8, 1.8);
            Arrow(dc, ink.Red, 13.5, 18.5, 28.5, 4.5, 2.4);
        }

        private static void PaintEffortRoute(M.DrawingContext dc, Ink ink)
        {
            dc.DrawGeometry(null, Ink.Pen(ink.Line, 1.8), G("M3,27 L12,15 L21,21 L29,9"));
            Arrow(dc, ink.Red, 12, 15, 7, 4, 2.0);
            Arrow(dc, ink.Red, 21, 21, 26, 30, 2.0);
            dc.DrawEllipse(ink.Blue, null, new W.Point(12, 15), 2, 2);
            dc.DrawEllipse(ink.Blue, null, new W.Point(21, 21), 2, 2);
        }

        private static void PaintParameters(M.DrawingContext dc, Ink ink)
        {
            // Poste com a altura do cabo marcada (cota) e o cabo saindo para o lado
            dc.DrawRectangle(ink.Line, null, new W.Rect(13, 3, 4, 26));
            dc.DrawLine(Ink.Pen(ink.Fiber, 2.2), new W.Point(17, 13), new W.Point(30, 15));
            M.Pen dim = Ink.Pen(ink.Blue, 1.4);
            dc.DrawLine(dim, new W.Point(6, 13), new W.Point(6, 29));
            dc.DrawLine(dim, new W.Point(3.5, 13), new W.Point(12, 13));
            dc.DrawLine(dim, new W.Point(3.5, 29), new W.Point(12, 29));
            dc.DrawEllipse(ink.Yellow, Ink.Pen(ink.YellowDark, 0.8), new W.Point(15, 13), 2.4, 2.4);
        }

        private static void PaintExistingEffort(M.DrawingContext dc, Ink ink)
        {
            // Poste visto de cima com a seta cinza (existente) e a vermelha (projeto)
            dc.DrawEllipse(ink.Paper, Ink.Pen(ink.Line, 1.8), new W.Point(10, 22), 5.5, 5.5);
            dc.DrawEllipse(ink.Line, null, new W.Point(10, 22), 1.8, 1.8);
            Arrow(dc, ink.Line, 12, 17, 12, 2.5, 2.2);
            Arrow(dc, ink.Red, 14.5, 19, 29, 7, 2.2);
        }

        private static void PaintReport(M.DrawingContext dc, Ink ink)
        {
            dc.DrawGeometry(ink.Paper, Ink.Pen(ink.Line, 1.3), G("M7,3 H20 L26,9 V29 H7 Z"));
            dc.DrawGeometry(null, Ink.Pen(ink.Line, 1.1), G("M20,3 V9 H26"));
            // Planilha
            dc.DrawRectangle(ink.Green, null, new W.Rect(10, 13, 13, 4));
            M.Pen grid = Ink.Pen(ink.Green, 1.2);
            dc.DrawRectangle(null, grid, new W.Rect(10, 13, 13, 13));
            dc.DrawLine(grid, new W.Point(10, 21.5), new W.Point(23, 21.5));
            dc.DrawLine(grid, new W.Point(14.5, 17), new W.Point(14.5, 26));
            dc.DrawLine(grid, new W.Point(18.8, 17), new W.Point(18.8, 26));
        }

        private static void PaintCheck(M.DrawingContext dc, Ink ink)
        {
            // Prancheta com itens conferidos (verde) e um pendente (vermelho)
            dc.DrawRoundedRectangle(ink.Paper, Ink.Pen(ink.Line, 1.3), new W.Rect(5, 4, 22, 26), 2, 2);
            dc.DrawRectangle(ink.Line, null, new W.Rect(11, 2, 10, 4));
            M.Pen ok = Ink.Pen(ink.Green, 2.0), text = Ink.Pen(ink.Line, 1.2);
            dc.DrawGeometry(null, ok, G("M8,11.5 L10,13.5 L13.5,9.5"));
            dc.DrawLine(text, new W.Point(16, 11.5), new W.Point(24, 11.5));
            dc.DrawGeometry(null, ok, G("M8,18 L10,20 L13.5,16"));
            dc.DrawLine(text, new W.Point(16, 18), new W.Point(24, 18));
            M.Pen bad = Ink.Pen(ink.Red, 2.0);
            dc.DrawLine(bad, new W.Point(8.5, 22.5), new W.Point(12.5, 26.5));
            dc.DrawLine(bad, new W.Point(12.5, 22.5), new W.Point(8.5, 26.5));
            dc.DrawLine(text, new W.Point(16, 24.5), new W.Point(24, 24.5));
        }

        private static void PaintMemorial(M.DrawingContext dc, Ink ink)
        {
            // Documento: folha com faixa azul, linhas de texto e selo verde de aprovado
            dc.DrawGeometry(ink.Paper, Ink.Pen(ink.Line, 1.3), G("M5,3 H19 L25,9 V29 H5 Z"));
            dc.DrawGeometry(null, Ink.Pen(ink.Line, 1.1), G("M19,3 V9 H25"));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(8, 11, 13, 3));
            M.Pen text = Ink.Pen(ink.Line, 1.2);
            dc.DrawLine(text, new W.Point(8, 17.5), new W.Point(21, 17.5));
            dc.DrawLine(text, new W.Point(8, 21), new W.Point(17, 21));
            dc.DrawEllipse(ink.Green, Ink.Pen(ink.Paper, 1.2), new W.Point(24, 24.5), 6, 6);
            dc.DrawGeometry(null, Ink.Pen(ink.White, 1.8), G("M21,24.6 L23.2,26.8 L27,22.6"));
        }

        private static void PaintEffortMemorial(M.DrawingContext dc, Ink ink)
        {
            // Documento com o poste visto de cima e a seta vermelha do esforço
            dc.DrawGeometry(ink.Paper, Ink.Pen(ink.Line, 1.3), G("M5,3 H19 L25,9 V29 H5 Z"));
            dc.DrawGeometry(null, Ink.Pen(ink.Line, 1.1), G("M19,3 V9 H25"));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(8, 11, 13, 3));
            M.Pen text = Ink.Pen(ink.Line, 1.2);
            dc.DrawLine(text, new W.Point(8, 17.5), new W.Point(16, 17.5));
            dc.DrawLine(text, new W.Point(8, 21), new W.Point(14, 21));
            dc.DrawEllipse(ink.Paper, Ink.Pen(ink.Line, 1.4), new W.Point(17, 25), 3.2, 3.2);
            Arrow(dc, ink.Red, 19, 23, 30, 12, 2.2);
        }

        private static void PaintNorm(M.DrawingContext dc, Ink ink)
        {
            // Livro da norma: capa azul com a faixa branca do título e o marcador vermelho
            dc.DrawRoundedRectangle(ink.Paper, Ink.Pen(ink.Line, 1.3), new W.Rect(8, 4, 19, 25), 1.5, 1.5);
            dc.DrawRoundedRectangle(ink.Blue, Ink.Pen(ink.Line, 1.3), new W.Rect(5, 3, 19, 25), 1.5, 1.5);
            dc.DrawRectangle(ink.White, null, new W.Rect(8.5, 8, 12, 4.5));
            M.Pen text = Ink.Pen(ink.Blue, 1.1);
            dc.DrawLine(text, new W.Point(10.5, 10.3), new W.Point(18.5, 10.3));
            M.Pen lines = Ink.Pen(ink.White, 1.1);
            dc.DrawLine(lines, new W.Point(8.5, 17), new W.Point(20.5, 17));
            dc.DrawLine(lines, new W.Point(8.5, 20.5), new W.Point(17, 20.5));
            dc.DrawGeometry(ink.Red, null, G("M18,3 H22 V12 L20,10 L18,12 Z"));
        }

        private static void PaintPoleCoordinates(M.DrawingContext dc, Ink ink)
        {
            // Documento com a tabela de coordenadas e o marcador de mapa
            dc.DrawGeometry(ink.Paper, Ink.Pen(ink.Line, 1.3), G("M5,3 H19 L25,9 V29 H5 Z"));
            dc.DrawGeometry(null, Ink.Pen(ink.Line, 1.1), G("M19,3 V9 H25"));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(8, 11, 13, 3));
            M.Pen grid = Ink.Pen(ink.Line, 1.1);
            for (int i = 0; i < 3; i++) dc.DrawLine(grid, new W.Point(8, 17.5 + i * 3.5), new W.Point(21, 17.5 + i * 3.5));
            dc.DrawLine(grid, new W.Point(12.5, 15.5), new W.Point(12.5, 25.5));
            dc.DrawGeometry(ink.Red, Ink.Pen(ink.Paper, 1.0), G("M25,15 C21,15 20,18.5 21,20.5 L25,28 L29,20.5 C30,18.5 29,15 25,15 Z"));
            dc.DrawEllipse(ink.White, null, new W.Point(25, 19), 1.6, 1.6);
        }

        /// <summary>Engrenagem com o centro azul e três amostras de cor (cabos) no canto.</summary>
        private static void PaintSettings(M.DrawingContext dc, Ink ink)
        {
            const double cx = 14, cy = 14;
            var gear = new M.StreamGeometry();
            using (M.StreamGeometryContext g = gear.Open())
            {
                bool first = true;
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4;
                    foreach (var (offset, radius) in new[] { (-0.38, 8.6), (-0.2, 12.2), (0.2, 12.2), (0.38, 8.6) })
                    {
                        var p = new W.Point(cx + radius * Math.Cos(a + offset), cy + radius * Math.Sin(a + offset));
                        if (first) g.BeginFigure(p, true, true);
                        else g.LineTo(p, true, true);
                        first = false;
                    }
                }
            }
            gear.Freeze();
            dc.DrawGeometry(ink.Paper, Ink.Pen(ink.Line, 1.3), gear);
            dc.DrawEllipse(ink.Blue, Ink.Pen(ink.Line, 1.1), new W.Point(cx, cy), 3.6, 3.6);

            dc.DrawRectangle(ink.Green, Ink.Pen(ink.Line, 0.8), new W.Rect(20, 22, 4, 7));
            dc.DrawRectangle(ink.Red, Ink.Pen(ink.Line, 0.8), new W.Rect(24, 22, 4, 7));
            dc.DrawRectangle(ink.Fiber, Ink.Pen(ink.Line, 0.8), new W.Rect(28, 22, 3, 7));
        }

        private static void PaintSheets(M.DrawingContext dc, Ink ink)
        {
            dc.DrawRectangle(ink.Paper, Ink.Pen(ink.Line, 1.3), new W.Rect(3, 6, 26, 21));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(6, 9, 9, 6.5));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(17, 9, 9, 6.5));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(6, 17.5, 9, 6.5));
            dc.DrawRectangle(ink.Blue, null, new W.Rect(17, 17.5, 9, 6.5));
        }

        /// <summary>Globo com marcador (Google Earth); a seta verde entra (importar) ou sai (exportar).</summary>
        private static void PaintEarth(M.DrawingContext dc, Ink ink)
        {
            dc.DrawEllipse(ink.Blue, null, new W.Point(13, 17), 11, 11);
            M.Pen grid = Ink.Pen(ink.White, 1.1);
            dc.DrawEllipse(null, grid, new W.Point(13, 17), 4.5, 11);
            dc.DrawLine(grid, new W.Point(2.5, 17), new W.Point(23.5, 17));
            dc.DrawLine(grid, new W.Point(4, 11.5), new W.Point(22, 11.5));
            dc.DrawLine(grid, new W.Point(4, 22.5), new W.Point(22, 22.5));
            dc.DrawGeometry(ink.Red, Ink.Pen(ink.White, 0.8), G("M13,4 C9,4 8,7.5 9,9.5 L13,16 L17,9.5 C18,7.5 17,4 13,4 Z"));
            dc.DrawEllipse(ink.White, null, new W.Point(13, 7.6), 1.5, 1.5);
        }

        private static void PaintKmlImport(M.DrawingContext dc, Ink ink)
        {
            PaintEarth(dc, ink);
            Arrow(dc, ink.Green, 30, 4, 21.5, 12.5, 2.4);
        }

        private static void PaintKmlExport(M.DrawingContext dc, Ink ink)
        {
            PaintEarth(dc, ink);
            Arrow(dc, ink.Green, 21.5, 12.5, 30, 4, 2.4);
        }

        private static void PaintRoads(M.DrawingContext dc, Ink ink)
        {
            // Cruzamento de duas ruas (asfalto com faixa amarela) e a seta verde de importar
            M.Pen asphalt = Ink.Pen(ink.Line, 7);
            dc.DrawLine(asphalt, new W.Point(3, 21), new W.Point(29, 21));
            dc.DrawLine(asphalt, new W.Point(11, 5), new W.Point(11, 29));
            var lane = new M.Pen(ink.Yellow, 1.3) { DashStyle = new M.DashStyle(new[] { 1.6, 1.4 }, 0) };
            dc.DrawLine(lane, new W.Point(15.5, 21), new W.Point(29, 21));
            dc.DrawLine(lane, new W.Point(11, 5), new W.Point(11, 16.5));
            Arrow(dc, ink.Green, 30, 3, 21.5, 11.5, 2.4);
        }
    }
}
