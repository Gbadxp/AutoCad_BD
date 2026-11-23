using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Pt = System.Windows.Point;

namespace Animacoes;

/// <summary>
/// Uma animação: o fundo do Model, as camadas (cada uma desenha o que for no instante t), o cursor do AutoCAD andando e
/// clicando e a linha de comando embaixo. Coordenadas em 320 x 180; o vídeo sai em 1,5 vez isso.
/// </summary>
internal sealed class Scene
{
    public const double W = 320, H = 180, BarHeight = 20;

    private readonly List<Action<DrawingContext, double>> _layers = new();
    private readonly List<(double At, string Text)> _prompts = new();
    private readonly List<(double At, Pt Point)> _path = new();
    private readonly List<double> _clicks = new();

    public Scene(string command, string title, double duration)
    {
        Command = command;
        Title = title;
        Duration = duration;
    }

    public string Command { get; }
    public string Title { get; }
    public double Duration { get; }

    /// <summary>Camada desenhada a cada quadro, na ordem em que foi acrescentada.</summary>
    public void Layer(Action<DrawingContext, double> draw) => _layers.Add(draw);

    /// <summary>Camada por cima de todas as outras (janelas de escolha), também na ordem em que foi acrescentada.</summary>
    public void Overlay(Action<DrawingContext, double> draw) => _overlays.Add(draw);

    private readonly List<Action<DrawingContext, double>> _overlays = new();

    /// <summary>Texto da linha de comando a partir do instante.</summary>
    public void Prompt(double at, string text) => _prompts.Add((at, text));

    /// <summary>O cursor chega ao ponto no instante (anda suave desde a posição anterior).</summary>
    public void Move(double at, Pt point) => _path.Add((at, point));

    /// <summary>O cursor chega ao ponto e clica.</summary>
    public void Click(double at, Pt point)
    {
        Move(at, point);
        _clicks.Add(at);
    }

    /// <summary>Onde o cursor está no instante (null antes de ele aparecer).</summary>
    public Pt? CursorAt(double t)
    {
        if (_path.Count == 0 || t < _path[0].At - 0.4) return null;
        var ordered = _path.OrderBy(p => p.At).ToList();
        if (t <= ordered[0].At) return ordered[0].Point;
        for (int i = 1; i < ordered.Count; i++)
        {
            if (t > ordered[i].At) continue;
            var (t0, p0) = ordered[i - 1];
            var (t1, p1) = ordered[i];
            double k = Draw.Smooth((t - t0) / Math.Max(1e-6, t1 - t0));
            return new Pt(p0.X + (p1.X - p0.X) * k, p0.Y + (p1.Y - p0.Y) * k);
        }
        return ordered[^1].Point;
    }

    public void Render(DrawingContext dc, double t)
    {
        dc.DrawRectangle(Draw.Background, null, new Rect(0, 0, W, H));
        for (double x = 10; x < W; x += 20) dc.DrawLine(Draw.GridPen, new Pt(x, 0), new Pt(x, H - BarHeight));
        for (double y = 10; y < H - BarHeight; y += 20) dc.DrawLine(Draw.GridPen, new Pt(0, y), new Pt(W, y));

        foreach (var layer in _layers) layer(dc, t);
        foreach (var overlay in _overlays) overlay(dc, t);

        // Clique: um anel que abre e some
        foreach (double at in _clicks)
        {
            double k = (t - at) / 0.45;
            if (k < 0 || k > 1 || CursorAt(at) is not Pt c) continue;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(220 * (1 - k)), 0x4F, 0xC3, 0xF7)), 1.2);
            dc.DrawEllipse(null, pen, c, 3 + 9 * k, 3 + 9 * k);
        }
        if (CursorAt(t) is Pt cursor) Draw.Crosshair(dc, cursor);

        // Nome do comando em cima, à esquerda
        var title = Draw.Text(Title, 8, Draw.White, bold: true);
        dc.DrawRoundedRectangle(Draw.Chip, null, new Rect(6, 5, title.Width + 12, 14), 3, 3);
        dc.DrawText(title, new Pt(12, 7));

        // Linha de comando embaixo, como a do AutoCAD
        dc.DrawRectangle(Draw.Bar, null, new Rect(0, H - BarHeight, W, BarHeight));
        dc.DrawLine(new Pen(Draw.BarLine, 0.8), new Pt(0, H - BarHeight), new Pt(W, H - BarHeight));
        string prompt = _prompts.Where(p => p.At <= t).OrderBy(p => p.At).Select(p => p.Text).LastOrDefault() ?? "";
        var text = Draw.Text("› " + prompt, 7, Draw.BarText);
        text.MaxTextWidth = W - 12;
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(text, new Pt(6, H - BarHeight + (BarHeight - text.Height) / 2));
    }
}

/// <summary>Cores e desenhos usados pelas cenas.</summary>
internal static class Draw
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public const double PixelsPerDip = 1.5;

    public static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x21, 0x28, 0x30)));
    public static readonly Pen GridPen = FreezePen(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C)), 0.5));
    public static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1)));
    public static readonly Brush CableRed = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x2A)));
    public static readonly Brush CableGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x3D)));
    public static readonly Brush Backbone = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x7B, 0xF5)));
    public static readonly Brush Strand = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0x9A, 0x23)));
    public static readonly Brush Effort = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xD8, 0xE6)));
    public static readonly Brush Yellow = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xD0, 0x42)));
    public static readonly Brush Blue = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x9B, 0xE8)));
    public static readonly Brush Highlight = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x4F, 0xC3, 0xF7)));
    public static readonly Brush Chip = Freeze(new SolidColorBrush(Color.FromArgb(0xD0, 0x0E, 0x6E, 0xB8)));
    public static readonly Brush Bar = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x21)));
    public static readonly Brush BarLine = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x44, 0x50)));
    public static readonly Brush BarText = Freeze(new SolidColorBrush(Color.FromRgb(0xD6, 0xDC, 0xE3)));
    public static readonly Brush Panel = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x3B, 0x47)));
    public static readonly Brush PanelLine = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x63)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public static Pen Pen(Brush brush, double width, bool dashed = false)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (dashed) pen.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);
        return pen;
    }

    public static FormattedText Text(string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, bold ? Bold : Face, size, brush, PixelsPerDip);

    /// <summary>0 a 1 entre os instantes a e b (0 antes, 1 depois).</summary>
    public static double Progress(double t, double a, double b) => b <= a ? (t >= a ? 1 : 0) : Math.Clamp((t - a) / (b - a), 0, 1);

    public static double Smooth(double k) { k = Math.Clamp(k, 0, 1); return k * k * (3 - 2 * k); }

    public static Pt Lerp(Pt a, Pt b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);

    /// <summary>Cursor em cruz do AutoCAD, com o quadradinho de seleção.</summary>
    public static void Crosshair(DrawingContext dc, Pt p)
    {
        var pen = new Pen(White, 0.7);
        dc.DrawLine(pen, new Pt(p.X - 9, p.Y), new Pt(p.X + 9, p.Y));
        dc.DrawLine(pen, new Pt(p.X, p.Y - 9), new Pt(p.X, p.Y + 9));
        dc.DrawRectangle(null, pen, new Rect(p.X - 2.2, p.Y - 2.2, 4.4, 4.4));
    }

    /// <summary>Texto com o ponto de encaixe: 0 = esquerda/cima, 0.5 = meio, 1 = direita/baixo.</summary>
    public static void Label(DrawingContext dc, string text, Pt at, double size, Brush brush, double alignX = 0, double alignY = 0,
        double angleDeg = 0, bool bold = false, double opacity = 1)
    {
        if (opacity <= 0 || text.Length == 0) return;
        var ft = Text(text, size, brush, bold);
        if (opacity < 1) dc.PushOpacity(opacity);
        if (angleDeg != 0) dc.PushTransform(new RotateTransform(angleDeg, at.X, at.Y));
        dc.DrawText(ft, new Pt(at.X - ft.Width * alignX, at.Y - ft.Height * alignY));
        if (angleDeg != 0) dc.Pop();
        if (opacity < 1) dc.Pop();
    }

    /// <summary>Texto de várias linhas (como o MText do poste).</summary>
    public static void Lines(DrawingContext dc, IReadOnlyList<string> lines, Pt topLeft, double size, Brush brush, double opacity = 1,
        Brush? highlight = null, int highlightLine = -1)
    {
        double y = topLeft.Y;
        for (int i = 0; i < lines.Count; i++)
        {
            var ft = Text(lines[i], size, brush);
            if (i == highlightLine && highlight != null) dc.DrawRoundedRectangle(highlight, null, new Rect(topLeft.X - 1.5, y - 0.5, ft.Width + 3, ft.Height + 1), 1.5, 1.5);
            if (opacity < 1) dc.PushOpacity(opacity);
            dc.DrawText(ft, new Pt(topLeft.X, y));
            if (opacity < 1) dc.Pop();
            y += ft.Height * 0.92;
        }
    }

    /// <summary>Poste: círculo com o ponto no meio (CC) ou o "I" do duplo T (DT), no tamanho dado.</summary>
    public static void Pole(DrawingContext dc, Pt p, double size = 1, bool doubleT = false, double rotationDeg = 0, Brush? color = null, double opacity = 1)
    {
        if (opacity <= 0) return;
        var pen = new Pen(color ?? White, 1.1 * size);
        if (opacity < 1) dc.PushOpacity(opacity);
        if (doubleT)
        {
            dc.PushTransform(new RotateTransform(rotationDeg, p.X, p.Y));
            double w = 5 * size, h = 3.2 * size;
            dc.DrawRectangle(Background, pen, new Rect(p.X - w, p.Y - h, 2 * w, 2 * h));
            dc.DrawLine(pen, new Pt(p.X - w, p.Y), new Pt(p.X + w, p.Y));
            dc.Pop();
        }
        else
        {
            dc.DrawEllipse(Background, pen, p, 4.5 * size, 4.5 * size);
            dc.DrawEllipse(color ?? White, null, p, 1.2 * size, 1.2 * size);
        }
        if (opacity < 1) dc.Pop();
    }

    /// <summary>Texto do poste no alto (número e 11/300), à direita ou, com left, à esquerda (seta de esforço saindo para a direita).</summary>
    public static void PoleLabel(DrawingContext dc, Pt pole, IReadOnlyList<string> lines, double opacity = 1, int highlightLine = -1, double size = 1,
        bool left = false)
    {
        double x = left ? pole.X - 5 * size - lines.Max(l => Text(l, 7 * size, White).Width) : pole.X + 5 * size;
        Lines(dc, lines, new Pt(x, pole.Y - 6 - lines.Count * 7.5 * size), 7 * size, White, opacity, Highlight, highlightLine);
    }

    /// <summary>Trecho de linha que vai sendo desenhado (k de 0 a 1).</summary>
    public static void Segment(DrawingContext dc, Pt a, Pt b, double k, Pen pen)
    {
        if (k <= 0) return;
        dc.DrawLine(pen, a, Lerp(a, b, Math.Min(1, k)));
    }

    /// <summary>Polilinha que vai sendo desenhada pelo comprimento (k de 0 a 1).</summary>
    public static void Path(DrawingContext dc, IReadOnlyList<Pt> points, double k, Pen pen)
    {
        if (k <= 0 || points.Count < 2) return;
        double total = 0;
        for (int i = 1; i < points.Count; i++) total += (points[i] - points[i - 1]).Length;
        double left = total * Math.Min(1, k);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, false);
            for (int i = 1; i < points.Count && left > 0; i++)
            {
                double len = (points[i] - points[i - 1]).Length;
                Pt end = len <= left ? points[i] : Lerp(points[i - 1], points[i], left / len);
                g.LineTo(end, true, true);
                left -= len;
            }
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>Nome do cabo acima do vão e metragem embaixo, alinhados com ele.</summary>
    public static void SpanTexts(DrawingContext dc, Pt a, Pt b, string? name, string length, double opacity, Brush? brush = null)
    {
        if (opacity <= 0) return;
        Pt mid = Lerp(a, b, 0.5);
        double angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
        if (angle > 90) angle -= 180;
        if (angle < -90) angle += 180;
        double rad = angle * Math.PI / 180;
        var up = new Vector(Math.Sin(rad), -Math.Cos(rad)) * 2.5;
        if (name != null) Label(dc, name, mid + up, 6, brush ?? CableGreen, 0.5, 1, angle, opacity: opacity);
        Label(dc, length, mid - up, 6, brush ?? CableGreen, 0.5, 0, angle, opacity: opacity);
    }

    /// <summary>CTO (quadrado verde) ou CEO (losango azul), com o texto embaixo.</summary>
    public static void Box(DrawingContext dc, Pt p, bool cto, IReadOnlyList<string> label, double size = 1, double opacity = 1, int highlightLine = -1)
    {
        if (opacity <= 0) return;
        if (opacity < 1) dc.PushOpacity(opacity);
        double s = 5 * size;
        if (cto)
        {
            dc.DrawRoundedRectangle(Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x8F, 0x4A))), new Pen(White, 0.8), new Rect(p.X - s, p.Y - s, 2 * s, 2 * s), 1.5, 1.5);
            Label(dc, "CTO", p, 3.4 * size, White, 0.5, 0.5, bold: true);
        }
        else
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Pt(p.X, p.Y - s * 1.2), true, true);
                c.LineTo(new Pt(p.X + s * 1.2, p.Y), true, true);
                c.LineTo(new Pt(p.X, p.Y + s * 1.2), true, true);
                c.LineTo(new Pt(p.X - s * 1.2, p.Y), true, true);
            }
            dc.DrawGeometry(Blue, new Pen(White, 0.8), g);
            Label(dc, "CEO", p, 3 * size, White, 0.5, 0.5, bold: true);
        }
        double y = p.Y + s * 1.3 + 1;
        for (int i = 0; i < label.Count; i++)
        {
            var ft = Text(label[i], 6.5, Yellow);
            if (i == highlightLine) dc.DrawRoundedRectangle(Highlight, null, new Rect(p.X - ft.Width / 2 - 1.5, y - 0.5, ft.Width + 3, ft.Height + 1), 1.5, 1.5);
            dc.DrawText(ft, new Pt(p.X - ft.Width / 2, y));
            y += ft.Height * 0.92;
        }
        if (opacity < 1) dc.Pop();
    }

    /// <summary>Seta de esforço saindo do poste, com o esforço em cima e o ângulo embaixo.</summary>
    public static void EffortArrow(DrawingContext dc, Pt pole, double angleDeg, double k, string effort, string angle)
    {
        if (k <= 0) return;
        double rad = angleDeg * Math.PI / 180;
        var dir = new Vector(Math.Cos(rad), -Math.Sin(rad));
        Pt tail = pole + dir * 7, tip = tail + dir * 34 * Smooth(k);
        var pen = new Pen(Effort, 1.2);
        dc.DrawLine(pen, tail, tip);
        var normal = new Vector(-dir.Y, dir.X);
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(tip, true, true);
            g.LineTo(tip - dir * 6 + normal * 2.6, true, true);
            g.LineTo(tip - dir * 6 - normal * 2.6, true, true);
        }
        dc.DrawGeometry(Effort, null, head);
        double textAngle = -angleDeg;
        if (textAngle > 90) textAngle -= 180;
        if (textAngle < -90) textAngle += 180;
        double tr = textAngle * Math.PI / 180;
        var up = new Vector(Math.Sin(tr), -Math.Cos(tr)) * 2;
        Pt mid = tail + dir * 15;
        double o = Progress(k, 0.6, 1);
        Label(dc, effort, mid + up, 5.8, Effort, 0.5, 1, textAngle, opacity: o);
        Label(dc, angle, mid - up, 5.8, Effort, 0.5, 0, textAngle, opacity: o);
    }

    /// <summary>Janela de escolha do plugin (lista com o item escolhido em destaque).</summary>
    public static void Picker(DrawingContext dc, Rect r, string title, IReadOnlyList<string> items, int selected, double opacity)
    {
        if (opacity <= 0) return;
        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(Panel, new Pen(PanelLine, 0.8), r, 3, 3);
        Label(dc, title, new Pt(r.X + 6, r.Y + 4), 6.5, White, bold: true);
        double y = r.Y + 16;
        for (int i = 0; i < items.Count; i++)
        {
            if (i == selected) dc.DrawRoundedRectangle(Blue, null, new Rect(r.X + 4, y - 1, r.Width - 8, 10), 2, 2);
            Label(dc, items[i], new Pt(r.X + 8, y), 6.2, i == selected ? White : BarText);
            y += 11;
        }
        dc.Pop();
    }

    /// <summary>Janela de seleção do AutoCAD (da esquerda para a direita: azul, linha cheia).</summary>
    public static void Window(DrawingContext dc, Pt a, Pt b, double opacity)
    {
        if (opacity <= 0) return;
        dc.PushOpacity(opacity);
        var r = new Rect(a, b);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x33, 0x2F, 0x7B, 0xF5)), new Pen(Backbone, 0.8), r);
        dc.Pop();
    }

    /// <summary>Linha elástica do último ponto até o cursor (tracejada).</summary>
    public static void Rubber(DrawingContext dc, Pt from, Pt to, Brush brush) => dc.DrawLine(Pen(brush, 0.8, dashed: true), from, to);

    /// <summary>Transformador (dois círculos), símbolo dos itens elétricos.</summary>
    public static void Transformer(DrawingContext dc, Pt p, double opacity)
    {
        if (opacity <= 0) return;
        dc.PushOpacity(opacity);
        var pen = new Pen(Yellow, 1);
        dc.DrawEllipse(null, pen, new Pt(p.X - 2.6, p.Y), 4.2, 4.2);
        dc.DrawEllipse(null, pen, new Pt(p.X + 2.6, p.Y), 4.2, 4.2);
        Label(dc, "TRAFO", new Pt(p.X, p.Y + 6), 5.5, Yellow, 0.5, 0);
        dc.Pop();
    }

    /// <summary>Amarração: o laço junto do poste, aberto para o lado do cabo (ângulo em graus, sentido do cabo).</summary>
    public static void Anchoring(DrawingContext dc, Pt pole, double angleDeg, double k)
    {
        if (k <= 0) return;
        double rad = angleDeg * Math.PI / 180;
        var dir = new Vector(Math.Cos(rad), Math.Sin(rad));
        var normal = new Vector(-dir.Y, dir.X);
        Pt c = pole + dir * 8;
        double s = 2.6 * Smooth(k);
        var pen = new Pen(Yellow, 0.9);
        var g = new StreamGeometry();
        using (var f = g.Open())
        {
            f.BeginFigure(c + dir * 5 + normal * s, false, false);
            f.LineTo(c + normal * s, true, true);
            f.ArcTo(c - normal * s, new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, true);
            f.LineTo(c + dir * 5 - normal * s, true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }
}
