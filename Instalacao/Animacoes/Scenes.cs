using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Pt = System.Windows.Point;

namespace Animacoes;

/// <summary>
/// As animações de cada comando de desenho e edição: o que se clica, o que o plugin pergunta (os mesmos textos da linha
/// de comando) e o que aparece no desenho.
/// </summary>
internal static class Scenes
{
    // Rede de exemplo: quatro postes em zigue-zague
    private static readonly Pt A = new(40, 108), B = new(120, 90), C = new(200, 122), D = new(282, 80);
    private static readonly Pt[] Net = { A, B, C, D };
    private static readonly string[] SpanLengths = { "82.0m", "86.1m", "91.2m" };
    private const string Cable = "ASU-80 06F.O";

    private static string Pole(int number) => $"P-{number:D2}";

    public static List<Scene> All() => new()
    {
        LaunchCable(backbone: false), LaunchCable(backbone: true), AutoRoute(), Reserve(), Strand(),
        InsertPoles(), InsertBox(cto: true), InsertBox(cto: false), Splitter(), Electrical(), Anchoring(),
        NumberAuto(), NumberByClicks(), Renumber(), SwapCable(), SwapPole(), BlockSize(), Standardize(),
        RouteEffort(), PoleEffort()
    };

    // ---------- Peças comuns ----------

    /// <summary>Os postes da rede, com o texto de cada um (que pode mudar com o tempo).</summary>
    private static void Poles(Scene s, Func<int, double, string[]>? label = null, Func<int, double, int>? highlight = null, Pt[]? at = null,
        int labelLeft = -1)
    {
        Pt[] poles = at ?? Net;
        s.Layer((dc, t) =>
        {
            for (int i = 0; i < poles.Length; i++)
            {
                Draw.Pole(dc, poles[i]);
                Draw.PoleLabel(dc, poles[i], label?.Invoke(i, t) ?? new[] { Pole(i + 1), "11/300" }, highlightLine: highlight?.Invoke(i, t) ?? -1,
                    left: i == labelLeft);
            }
        });
    }

    /// <summary>O cabo já lançado, com nome e metragem nos vãos.</summary>
    private static void DrawnCable(Scene s, Func<double, Brush>? brush = null, Func<double, string>? name = null, Pt[]? at = null, bool texts = true)
    {
        Pt[] poles = at ?? Net;
        s.Layer((dc, t) =>
        {
            Draw.Path(dc, poles, 1, Draw.Pen(brush?.Invoke(t) ?? Draw.CableRed, 2.2));
            if (!texts) return;
            for (int i = 0; i < poles.Length - 1; i++)
                Draw.SpanTexts(dc, poles[i], poles[i + 1], i % 2 == 0 ? name?.Invoke(t) ?? Cable : null, SpanLengths[i % SpanLengths.Length], 1);
        });
    }

    /// <summary>Clica nos pontos, um de cada vez, com a linha elástica até o cursor (como o LINE).</summary>
    private static void ClickPath(Scene s, Pt[] points, double[] times, double end, Brush preview)
    {
        for (int i = 0; i < points.Length; i++) s.Click(times[i], points[i]);
        s.Layer((dc, t) =>
        {
            if (t >= end) return;
            int done = times.Count(at => at <= t);
            if (done >= 2) Draw.Path(dc, points.Take(done).ToArray(), 1, Draw.Pen(preview, 1.1));
            if (done >= 1 && s.CursorAt(t) is Pt c) Draw.Rubber(dc, points[done - 1], c, preview);
        });
    }

    private static readonly Rect PickerArea = new(92, 28, 136, 56);

    /// <summary>Janela de escolha (cabo, modelo, bloco): o cursor vai até o item e clica.</summary>
    private static void Pick(Scene s, string title, string[] items, int choice, double from, double click)
    {
        Pt item = new(PickerArea.X + 50, PickerArea.Y + 16 + choice * 11 + 4);
        s.Move(from + 0.2, new Pt(PickerArea.Right - 10, PickerArea.Bottom - 6));
        s.Click(click, item);
        s.Overlay((dc, t) =>
        {
            double opacity = Draw.Progress(t, from, from + 0.25) * (1 - Draw.Progress(t, click + 0.25, click + 0.5));
            Draw.Picker(dc, PickerArea, title, items, t >= click - 0.35 ? choice : -1, opacity);
        });
    }

    /// <summary>Ângulo da resultante (graus, anti-horário a partir do leste) no poste i de uma sequência.</summary>
    private static double Resultant(Pt[] poles, int i)
    {
        var v = new Vector(0, 0);
        if (i > 0) { var d = poles[i - 1] - poles[i]; d.Normalize(); v += d; }
        if (i < poles.Length - 1) { var d = poles[i + 1] - poles[i]; d.Normalize(); v += d; }
        double deg = Math.Atan2(-v.Y, v.X) * 180 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    private static string Angle(double deg) => "ANG. " + Math.Round(deg).ToString(CultureInfo.InvariantCulture) + "°";

    // ---------- Cabos ----------

    private static Scene LaunchCable(bool backbone)
    {
        string command = backbone ? "FIBRA_LANCAR_BACKBONE" : "FIBRA_LANCAR_CABO";
        string cable = backbone ? "ASU-120 36F.O" : Cable;
        var s = new Scene(command, backbone ? "Lançar Backbone" : "Lançar Cabo", 10);
        s.Prompt(0, "Comando: " + command);
        Pick(s, "Escolha o cabo", new[] { "ASU-80 06F.O", "ASU-80 12F.O", "ASU-120 36F.O" }, backbone ? 2 : 0, 0.3, 1.6);
        Poles(s);
        s.Prompt(2.1, $"Selecione o ponto inicial do {(backbone ? "backbone" : "cabo")}:");
        double[] clicks = { 2.8, 4.0, 5.2, 6.4 };
        ClickPath(s, Net, clicks, 7.4, Draw.CableGreen);
        s.Prompt(2.9, "Selecione o próximo ponto (ou pressione Enter para finalizar) [Desfazer]:");
        s.Prompt(7.4, $"[COMPRIMENTO TOTAL]: 259.30 metros.{(backbone ? "  Layer FIBRA_BACKBONE_ASU-120_36F.O" : "")}");
        s.Move(7.3, new Pt(300, 140));
        s.Layer((dc, t) =>
        {
            double k = Draw.Progress(t, 7.4, 7.8);
            if (k <= 0) return;
            dc.PushOpacity(k);
            Draw.Path(dc, Net, 1, Draw.Pen(backbone ? Draw.Backbone : Draw.CableRed, backbone ? 3.6 : 2.2));
            dc.Pop();
            for (int i = 0; i < Net.Length - 1; i++)
                Draw.SpanTexts(dc, Net[i], Net[i + 1], i % 2 == 0 ? cable : null, SpanLengths[i], Draw.Progress(t, 7.7, 8.2));
        });
        return s;
    }

    private static Scene AutoRoute()
    {
        var s = new Scene("FIBRA_ROTEAMENTO_AUTO", "Roteamento Automático", 9.5);
        s.Prompt(0, "Comando: FIBRA_ROTEAMENTO_AUTO");
        Pick(s, "Escolha o cabo", new[] { "ASU-80 06F.O", "ASU-80 12F.O", "ASU-120 36F.O" }, 0, 0.3, 1.5);
        Poles(s);
        s.Prompt(1.9, "Lado do cabo em relação aos postes [Cima/Baixo/Esquerda/Direita] <Cima>:");
        s.Prompt(2.9, "Selecione os elementos (blocos) para o roteamento automático:");
        Pt w1 = new(22, 52), w2 = new(304, 140);
        s.Click(3.4, w1);
        s.Click(4.6, w2);
        s.Layer((dc, t) =>
        {
            if (t < 3.4 || t > 5.0 || s.CursorAt(t) is not Pt c) return;
            Draw.Window(dc, w1, t < 4.6 ? c : w2, 1 - Draw.Progress(t, 4.7, 5.0));
        });
        Pt[] above = Net.Select(p => new Pt(p.X, p.Y - 6)).ToArray();
        s.Prompt(5.1, "Calculando a rota mais curta...");
        s.Prompt(6.9, "[SUCESSO]: Roteamento automático concluído! 4 pontos conectados, cabo do lado de cima.");
        s.Move(5.0, new Pt(300, 150));
        s.Layer((dc, t) =>
        {
            Draw.Path(dc, above, Draw.Smooth(Draw.Progress(t, 5.1, 6.8)), Draw.Pen(Draw.CableRed, 2.2));
            for (int i = 0; i < above.Length - 1; i++)
                Draw.SpanTexts(dc, above[i], above[i + 1], i % 2 == 0 ? Cable : null, SpanLengths[i], Draw.Progress(t, 6.7, 7.2));
        });
        return s;
    }

    private static Scene Reserve()
    {
        var s = new Scene("FIBRA_RESERVA_TECNICA", "Reserva Técnica", 8.5);
        Poles(s);
        DrawnCable(s);
        s.Prompt(0, "Comprimento da reserva técnica (m) <30>: 30");
        s.Prompt(1.6, "Clique no ponto da reserva (poste ou cabo):");
        s.Move(1.4, new Pt(160, 150));
        s.Click(2.6, B);
        s.Prompt(2.8, "Clique para o lado em que vai o texto da reserva:");
        Pt end = new(152, 40);
        s.Move(3.6, new Pt(100, 45));
        s.Click(4.8, end);
        s.Prompt(5.0, "[INFO]: RESERVA TÉCNICA 30m no poste P-02.");
        s.Move(5.6, new Pt(300, 150));
        s.Layer((dc, t) =>
        {
            if (t < 2.7) return;
            Pt tip = t < 4.8 && s.CursorAt(t) is Pt c ? c : end;
            var dir = B - tip;
            if (dir.Length < 8) return;
            dir.Normalize();
            Pt head = B - dir * 6;
            var pen = new Pen(Draw.Yellow, 1);
            dc.DrawLine(pen, tip, head);
            var normal = new Vector(-dir.Y, dir.X);
            var g = new StreamGeometry();
            using (var f = g.Open())
            {
                f.BeginFigure(head, true, true);
                f.LineTo(head - dir * 5 + normal * 2.2, true, true);
                f.LineTo(head - dir * 5 - normal * 2.2, true, true);
            }
            dc.DrawGeometry(Draw.Yellow, null, g);
            Draw.Label(dc, "RESERVA TÉCNICA 30m", tip + new Vector(0, -2), 6.5, Draw.Yellow, tip.X < B.X ? 1 : 0, 1);
        });
        return s;
    }

    private static Scene Strand()
    {
        var s = new Scene("FIBRA_CORDOALHA", "Lançar Cordoalha", 9);
        Poles(s);
        s.Prompt(0, "Selecione o ponto inicial da cordoalha:");
        double[] clicks = { 1.0, 2.2, 3.4, 4.6 };
        ClickPath(s, Net, clicks, 5.6, Draw.Strand);
        s.Prompt(1.1, "Selecione o próximo ponto (ou pressione Enter para finalizar) [Desfazer]:");
        s.Prompt(5.6, "[SUCESSO]: Cordoalha lançada: 259.30 m em 3 trecho(s).");
        s.Move(5.5, new Pt(300, 145));
        s.Layer((dc, t) =>
        {
            double k = Draw.Progress(t, 5.6, 6.0);
            if (k <= 0) return;
            dc.PushOpacity(k);
            Draw.Path(dc, Net, 1, Draw.Pen(Draw.Strand, 1.6, dashed: true));
            dc.Pop();
            for (int i = 0; i < Net.Length - 1; i++) Draw.SpanTexts(dc, Net[i], Net[i + 1], null, SpanLengths[i], Draw.Progress(t, 5.9, 6.4));
        });
        return s;
    }

    // ---------- Inserir ----------

    private static Scene InsertPoles()
    {
        var s = new Scene("FIBRA_INSERIR_POSTE", "Inserir Postes", 10.5);
        Pick(s, "Modelo do poste", new[] { "CC 9/200", "DT 11/300", "DT 11/600" }, 1, 0.2, 1.4);
        s.Prompt(0, "Comando: FIBRA_INSERIR_POSTE");
        s.Prompt(1.7, "Número do próximo poste <1>:");
        Pt[] at = { A, B, C };
        double[] place = { 2.8, 5.0, 7.2 }, turn = { 3.9, 6.1, 8.3 };
        Pt[] aim = { new(70, 92), new(152, 112), new(232, 100) };
        for (int i = 0; i < at.Length; i++)
        {
            s.Prompt(place[i] - 0.6, $"Poste {Pole(i + 1)} (DT 11/300): ponto de inserção [Modelo/Numero]:");
            s.Click(place[i], at[i]);
            s.Prompt(place[i] + 0.1, "Gire o poste com o mouse e clique (ou digite o ângulo) <0>:");
            s.Click(turn[i], aim[i]);
            s.Prompt(turn[i] + 0.1, $"[OK]: {Pole(i + 1)} | DT 11/300 | E 4{195 + i * 96}.21 N 9{449 - i * 12}.26");
        }
        s.Move(9.0, new Pt(300, 145));
        s.Layer((dc, t) =>
        {
            for (int i = 0; i < at.Length; i++)
            {
                if (t < place[i]) continue;
                Pt target = t < turn[i] && s.CursorAt(t) is Pt c ? c : aim[i];
                double rotation = Math.Atan2(target.Y - at[i].Y, target.X - at[i].X) * 180 / Math.PI;
                Draw.Pole(dc, at[i], doubleT: true, rotationDeg: rotation);
                Draw.PoleLabel(dc, at[i], new[] { Pole(i + 1), "11/300" }, Draw.Progress(t, turn[i], turn[i] + 0.3));
            }
        });
        return s;
    }

    private static Scene InsertBox(bool cto)
    {
        string kind = cto ? "CTO" : "CEO";
        var s = new Scene(cto ? "FIBRA_INSERIR_CTO" : "FIBRA_INSERIR_CEO", "Inserir " + kind, 8.5);
        Poles(s);
        DrawnCable(s);
        s.Prompt(0, $"Número da próxima {kind} <1>:");
        Pt[] at = { new(B.X + 2, B.Y + 18), new(D.X + 2, D.Y + 18) };
        double[] clicks = { 1.8, 4.2 };
        for (int i = 0; i < at.Length; i++)
        {
            s.Prompt(clicks[i] - 0.8, $"{kind}-0{i + 1} ({kind}): ponto de inserção [Modelo/Numero]:");
            s.Click(clicks[i], at[i]);
            s.Prompt(clicks[i] + 0.1, $"[OK]: {kind}-0{i + 1} ({kind}) | poste {(i == 0 ? "P-02" : "P-04")}");
        }
        s.Prompt(5.6, $"[INFO]: 2 {kind}(s) inserida(s), cada uma ligada ao poste mais próximo.");
        s.Move(5.4, new Pt(170, 150));
        s.Layer((dc, t) =>
        {
            for (int i = 0; i < at.Length; i++)
                Draw.Box(dc, at[i], cto, new[] { $"{kind}-0{i + 1}" }, opacity: Draw.Progress(t, clicks[i], clicks[i] + 0.25));
        });
        return s;
    }

    private static Scene Splitter()
    {
        var s = new Scene("FIBRA_SPLITTER", "Marcar Splitter", 9.5);
        Poles(s);
        DrawnCable(s);
        Pt box = new(B.X + 2, B.Y + 18);
        s.Prompt(0, "Clique na CTO/CEO do splitter (Enter para terminar) [Remover]:");
        s.Click(1.4, box);
        s.Prompt(1.5, "Nível do splitter na CTO-01 (1, 2 ou 3) <1>: 1");
        s.Prompt(2.5, "Razão do splitter 1:X (2, 4, 8, 16, 32 ou 64) <8>: 8");
        s.Prompt(3.5, "[OK]: CTO-01: S1 1:8");
        s.Click(4.8, box);
        s.Prompt(4.9, "Nível do splitter na CTO-01 (1, 2 ou 3) <1>: 2");
        s.Prompt(5.9, "Razão do splitter 1:X (2, 4, 8, 16, 32 ou 64) <8>: 4");
        s.Prompt(6.9, "[OK]: CTO-01: S1 1:8, S2 1:4");
        s.Move(7.4, new Pt(240, 150));
        s.Layer((dc, t) =>
        {
            var lines = new List<string> { "CTO-01" };
            if (t >= 3.5) lines.Add("S1 1:8");
            if (t >= 6.9) lines.Add("S2 1:4");
            int highlight = t >= 3.5 && t < 4.4 ? 1 : t >= 6.9 && t < 7.8 ? 2 : -1;
            Draw.Box(dc, box, true, lines, highlightLine: highlight);
        });
        return s;
    }

    private static Scene Electrical()
    {
        var s = new Scene("FIBRA_INSERIR_ELETRICOS", "Itens Elétricos", 7.5);
        Poles(s);
        DrawnCable(s);
        Pick(s, "Inserir Itens Elétricos", new[] { "CHAVE FUSÍVEL", "PARA-RAIO", "TRAFO" }, 2, 0.2, 1.5);
        s.Prompt(0, "Comando: FIBRA_INSERIR_ELETRICOS");
        Pt at = new(C.X + 20, C.Y + 20); // Abaixo do poste, longe do texto dele
        s.Prompt(1.9, "TRAFO: ponto de inserção [Bloco]:");
        s.Click(3.2, at);
        s.Prompt(3.3, "[OK]: TRAFO inserido.   (Enter termina; Bloco troca o item)");
        s.Move(4.4, new Pt(290, 150));
        s.Layer((dc, t) => Draw.Transformer(dc, at, Draw.Progress(t, 3.2, 3.45)));
        return s;
    }

    private static Scene Anchoring()
    {
        var s = new Scene("FIBRA_INSERIR_AMARRACAO", "Amarração", 8.5);
        Poles(s);
        DrawnCable(s);
        s.Prompt(0, "Selecione os postes a amarrar (Enter = inserir uma amarração à mão):");
        Pt w1 = new(24, 58), w2 = new(302, 136);
        s.Click(1.0, w1);
        s.Click(2.3, w2);
        s.Layer((dc, t) =>
        {
            if (t < 1.0 || t > 3.0 || s.CursorAt(t) is not Pt c) return;
            Draw.Window(dc, w1, t < 2.3 ? c : w2, 1 - Draw.Progress(t, 2.7, 3.0));
        });
        s.Prompt(2.6, "Uma amarração em cada direção de cabo que sai do poste...");
        s.Prompt(5.0, "[SUCESSO]: 6 amarração(ões) em 4 poste(s).");
        s.Move(3.0, new Pt(300, 150));
        s.Layer((dc, t) =>
        {
            int n = 0;
            for (int i = 0; i < Net.Length; i++)
            {
                foreach (int j in new[] { i - 1, i + 1 })
                {
                    if (j < 0 || j >= Net.Length) continue;
                    double angle = Math.Atan2(Net[j].Y - Net[i].Y, Net[j].X - Net[i].X) * 180 / Math.PI;
                    double start = 3.1 + 0.3 * n++;
                    Draw.Anchoring(dc, Net[i], angle, Draw.Progress(t, start, start + 0.3));
                }
            }
        });
        return s;
    }

    // ---------- Editar ----------

    private static Scene NumberAuto()
    {
        var s = new Scene("FIBRA_NUMERAR_AUTO", "Numerar Auto", 9);
        int[] before = { 7, 3, 12, 1 };
        double[] change = { 3.4, 3.9, 4.4, 4.9 };
        Poles(s, (i, t) => new[] { Pole(t >= change[i] ? i + 1 : before[i]), "11/300" },
              (i, t) => t >= change[i] && t < change[i] + 0.8 ? 0 : -1);
        DrawnCable(s);
        s.Prompt(0, "Clique no primeiro poste da numeração (4 postes do desenho) [Selecionar]:");
        s.Click(1.6, A);
        s.Prompt(1.7, "Número do primeiro poste <1>:");
        s.Prompt(2.8, "Numerando 4 poste(s): a numeração segue o cabo...");
        s.Prompt(5.4, "[SUCESSO]: 4 poste(s) numerado(s) de P-01 a P-04, seguindo os cabos a partir do P-01.");
        s.Move(2.6, new Pt(150, 150));
        s.Layer((dc, t) =>
        {
            double k = Draw.Progress(t, 3.4, 4.9);
            if (k <= 0 || k >= 1) return;
            double along = k * (Net.Length - 1);
            int seg = Math.Min(Net.Length - 2, (int)along);
            Pt p = Draw.Lerp(Net[seg], Net[seg + 1], along - seg);
            dc.DrawEllipse(Draw.Highlight, null, p, 6, 6);
            dc.DrawEllipse(Draw.Effort, null, p, 2.2, 2.2);
        });
        return s;
    }

    private static Scene NumberByClicks()
    {
        var s = new Scene("FIBRA_NUMERAR_MANUAL", "Numerar Clicando", 9);
        int[] before = { 3, 9, 1, 6 };
        double[] clicks = { 2.4, 3.6, 4.8, 6.0 };
        Poles(s, (i, t) => new[] { Pole(t >= clicks[i] ? 10 + i : before[i]), "11/300" },
              (i, t) => t >= clicks[i] && t < clicks[i] + 0.8 ? 0 : -1);
        DrawnCable(s);
        s.Prompt(0, "Número do primeiro poste clicado <1>: 10");
        for (int i = 0; i < Net.Length; i++)
        {
            s.Prompt(clicks[i] - 0.9, $"Clique no poste que vai ser P-{10 + i}{(i > 0 ? " ou Enter para terminar" : "")} [Desfazer/Numero]:");
            s.Click(clicks[i], Net[i]);
        }
        s.Prompt(6.9, "[SUCESSO]: 4 poste(s) numerado(s) na ordem dos cliques, de P-10 a P-13.");
        s.Move(7.0, new Pt(290, 150));
        return s;
    }

    private static Scene Renumber()
    {
        var s = new Scene("FIBRA_RENUMERAR", "Renumerar", 8.5);
        double[] clicks = { 1.4, 3.8, 5.0 };
        int[] number = { 5, 6, 7 };
        Poles(s, (i, t) => new[] { i >= 1 && t >= clicks[i - 1] + (i == 1 ? 1.0 : 0) ? Pole(number[i - 1]) : Pole(i + 1), "11/300" },
              (i, t) => i >= 1 && t >= clicks[i - 1] + (i == 1 ? 1.0 : 0) && t < clicks[i - 1] + 1.8 ? 0 : -1);
        DrawnCable(s);
        s.Prompt(0, "Selecione o bloco a renumerar [Numero]:");
        s.Click(clicks[0], B);
        s.Prompt(1.5, "Número novo para P-02 <2>: 5");
        s.Prompt(2.5, "[OK]: P-02 → P-05.   Os seguintes recebem o próximo número.");
        s.Click(clicks[1], C);
        s.Prompt(clicks[1] + 0.1, "[OK]: P-03 → P-06");
        s.Click(clicks[2], D);
        s.Prompt(clicks[2] + 0.1, "[OK]: P-04 → P-07");
        s.Prompt(6.2, "[INFO]: 3 bloco(s) renumerado(s).");
        s.Move(6.0, new Pt(290, 150));
        return s;
    }

    private static Scene SwapCable()
    {
        var s = new Scene("FIBRA_TROCAR_CABO", "Trocar Cabo", 8);
        var magenta = new SolidColorBrush(Color.FromRgb(0xD8, 0x4C, 0xF0));
        magenta.Freeze();
        Poles(s);
        DrawnCable(s, t => t >= 3.0 ? magenta : Draw.CableRed, t => t >= 3.0 ? "ASU-80 12F.O" : Cable);
        Pick(s, "Escolha o cabo", new[] { "ASU-80 06F.O", "ASU-80 12F.O", "ASU-120 36F.O" }, 1, 0.2, 1.4);
        s.Prompt(0, "Comando: FIBRA_TROCAR_CABO");
        s.Prompt(1.7, "Clique no cabo a trocar para ASU-80 12F.O (ou Enter para terminar):");
        s.Click(2.9, Draw.Lerp(B, C, 0.5));
        s.Prompt(3.0, "[INFO]: Cabo ASU-80 06F.O (259.3 m) trocado para ASU-80 12F.O, com 5 texto(s).");
        s.Prompt(5.2, "[AVISO]: A tração do ASU-80 12F.O é diferente: rode o Esforço no Percurso de novo.");
        s.Move(4.2, new Pt(290, 150));
        return s;
    }

    private static Scene SwapPole()
    {
        var s = new Scene("FIBRA_TROCAR_POSTE", "Trocar Poste", 8);
        double[] clicks = { 2.8, 4.4 };
        Pick(s, "Modelo do poste", new[] { "CC 9/200", "DT 11/300", "DT 11/600" }, 2, 0.2, 1.4);
        DrawnCable(s);
        s.Layer((dc, t) =>
        {
            for (int i = 0; i < Net.Length; i++)
            {
                int swap = i == 1 ? 0 : i == 2 ? 1 : -1;
                bool changed = swap >= 0 && t >= clicks[swap];
                Draw.Pole(dc, Net[i], doubleT: changed, rotationDeg: changed ? Math.Atan2(Net[i + 1].Y - Net[i - 1].Y, Net[i + 1].X - Net[i - 1].X) * 180 / Math.PI : 0);
                Draw.PoleLabel(dc, Net[i], new[] { Pole(i + 1), changed ? "11/600" : "11/300" },
                    highlightLine: changed && t < clicks[swap] + 0.9 ? 1 : -1);
            }
        });
        s.Prompt(0, "Comando: FIBRA_TROCAR_POSTE");
        s.Prompt(1.7, "Clique no poste a trocar para DT 11/600 (ou Enter para terminar):");
        s.Click(clicks[0], B);
        s.Prompt(clicks[0] + 0.1, "[INFO]: P-02: CC 11/300 trocado para DT 11/600.");
        s.Click(clicks[1], C);
        s.Prompt(clicks[1] + 0.1, "[INFO]: P-03: CC 11/300 trocado para DT 11/600.");
        s.Prompt(5.6, "[AVISO]: A altura do poste mudou: rode o Esforço no Percurso de novo.");
        s.Move(5.6, new Pt(290, 150));
        return s;
    }

    private static Scene BlockSize()
    {
        var s = new Scene("FIBRA_TAMANHO_BLOCO", "Tamanho do Bloco", 8);
        Poles(s);
        DrawnCable(s);
        Pt[] boxes = { new(B.X + 2, B.Y + 18), new(D.X + 2, D.Y + 18) };
        s.Prompt(0, "Selecione o(s) bloco(s) para mudar o tamanho:");
        Pt w1 = new(102, 98), w2 = new(304, 140);
        s.Click(0.9, w1);
        s.Click(2.1, w2);
        s.Layer((dc, t) =>
        {
            if (t < 0.9 || t > 2.8 || s.CursorAt(t) is not Pt c) return;
            Draw.Window(dc, w1, t < 2.1 ? c : w2, 1 - Draw.Progress(t, 2.5, 2.8));
        });
        s.Prompt(2.5, "Novo tamanho, maior lado em m [Fator] <3>: 6");
        s.Prompt(3.9, "[OK]: 2 bloco(s) com tamanho 6 m (maior lado).");
        s.Move(2.6, new Pt(180, 150));
        s.Layer((dc, t) =>
        {
            double size = 1 + Draw.Smooth(Draw.Progress(t, 3.7, 4.4)) * 0.8;
            for (int i = 0; i < boxes.Length; i++) Draw.Box(dc, boxes[i], true, new[] { $"CTO-0{i + 1}" }, size);
        });
        return s;
    }

    private static Scene Standardize()
    {
        var s = new Scene("FIBRA_PADRONIZAR_TAMANHOS", "Padronizar Tamanhos", 7.5);
        DrawnCable(s);
        double[] start = { 1, 2.1, 1, 0.55 };
        s.Layer((dc, t) =>
        {
            double k = Draw.Smooth(Draw.Progress(t, 2.0, 3.0));
            for (int i = 0; i < Net.Length; i++)
            {
                double size = start[i] + (1 - start[i]) * k;
                Draw.Pole(dc, Net[i], size);
                Draw.PoleLabel(dc, Net[i], new[] { Pole(i + 1), "11/300" }, size: size);
            }
        });
        s.Prompt(0, "Comando: FIBRA_PADRONIZAR_TAMANHOS   (postes colados de um desenho em outra escala)");
        s.Prompt(2.0, "Deixando postes, CTO/CEO, textos e setas no tamanho da escala do desenho...");
        s.Prompt(3.2, "[SUCESSO]: 4 elemento(s) no tamanho da escala 1:1000 (texto de 2 unidades).");
        return s;
    }

    // ---------- Esforços ----------

    // Percurso com um poste quase em linha (sem seta), um de ângulo e as duas pontas
    private static readonly Pt[] EffortNet = { new(36, 112), new(116, 104), new(196, 97), new(276, 136) };

    private static Scene RouteEffort()
    {
        var s = new Scene("FIBRA_ESFORCO_PERCURSO", "Esforço no Percurso", 10.5);
        Poles(s, at: EffortNet, labelLeft: 0); // A seta do P-01 sai para a direita, por cima do texto
        DrawnCable(s, at: EffortNet, texts: false); // Sem os textos dos vãos: as setas ficam à vista
        s.Prompt(0, "Esforço em cada poste [Total/Cabo] <Total>:");
        s.Prompt(1.2, "Selecione o(s) cabo(s) do percurso:");
        s.Click(2.4, Draw.Lerp(EffortNet[1], EffortNet[2], 0.5));
        s.Prompt(3.0, "P-02 | Passagem | 0.62 kgf no cabo | deflexão de 1°, sem seta (só acima de 10°)");
        s.Prompt(5.4, "[SUCESSO]: 4 ponto(s): 3 com seta (pontas e ângulo acima de 10°) e 1 sem seta (passagem).");
        s.Move(3.0, new Pt(160, 150));
        int[] withArrow = { 0, 2, 3 };
        string[] kgf = { "24.98 KGF", "19.87 KGF", "26.10 KGF" };
        s.Layer((dc, t) =>
        {
            for (int n = 0; n < withArrow.Length; n++)
            {
                int i = withArrow[n];
                double angle = Resultant(EffortNet, i);
                Draw.EffortArrow(dc, EffortNet[i], angle, Draw.Progress(t, 3.3 + 0.6 * n, 4.1 + 0.6 * n), kgf[n], Angle(angle));
            }
        });
        return s;
    }

    private static Scene PoleEffort()
    {
        var s = new Scene("FIBRA_ESFORCO_TOTAL", "Esforço no Poste", 8.5);
        Poles(s, at: EffortNet, labelLeft: 0); // A seta do P-01 sai para a direita, por cima do texto
        DrawnCable(s, at: EffortNet, texts: false); // Sem os textos dos vãos: as setas ficam à vista
        s.Prompt(0, "Clique no poste para calcular a Rota Total (Enter para sair):");
        s.Click(1.6, EffortNet[2]);
        double angle = Resultant(EffortNet, 2);
        s.Prompt(1.7, $"[SUCESSO]: Poste P-03 | Ângulo (34°) | 18.42 kgf no cabo (1 cabo(s)) | seta 19.87 KGF, {Angle(angle)}.");
        s.Prompt(3.6, "Poste P-03 | DT 11/300: 19.87 kgf a 20 cm do topo de 306 kgf nominal → OK (6%)");
        s.Click(5.2, EffortNet[3]);
        double end = Resultant(EffortNet, 3);
        s.Prompt(5.3, $"[SUCESSO]: Poste P-04 | Fim de rede | 24.10 kgf no cabo (1 cabo(s)) | seta 26.10 KGF, {Angle(end)}.");
        s.Move(6.6, new Pt(150, 150));
        s.Layer((dc, t) =>
        {
            Draw.EffortArrow(dc, EffortNet[2], angle, Draw.Progress(t, 1.7, 2.5), "19.87 KGF", Angle(angle));
            Draw.EffortArrow(dc, EffortNet[3], end, Draw.Progress(t, 5.3, 6.1), "26.10 KGF", Angle(end));
        });
        return s;
    }
}
