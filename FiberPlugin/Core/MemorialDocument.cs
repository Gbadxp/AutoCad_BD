using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>Dados do projeto para o Memorial Descritivo.</summary>
    public sealed class MemorialData
    {
        public CompanyInfo Company { get; set; } = null!;
        public string Route { get; set; } = "";          // Percurso da rede (também vai na plaqueta)
        public string WorkAddress { get; set; } = "";    // Endereço da obra
        public string PlaceAndDate { get; set; } = "";   // Ex.: Porto Velho/RO, 29 de setembro de 2026

        public int PoleCount { get; set; }
        public List<(string Type, string Description, int Count)> PoleTypes { get; set; } = new List<(string, string, int)>();
        public int CtoCount { get; set; }
        public int CeoCount { get; set; }
        public List<CableTotal> Cables { get; set; } = new List<CableTotal>();

        public double CableLength => Cables.Sum(c => c.Length);
    }

    /// <summary>
    /// Monta o Memorial Descritivo em HTML (folhas A4 prontas para virar PDF): capa, ofício à concessionária,
    /// memorial (empresa, objetivo, percurso, cabos, posteamento) e figuras de instalação.
    /// O logo e as figuras vêm de Dados\Memorial e vão embutidos no arquivo.
    /// </summary>
    public static class MemorialDocument
    {
        public const string AssetsFolder = "Memorial";

        private static readonly CultureInfo Br = new CultureInfo("pt-BR");

        public static string Html(MemorialData d, string? assetsDir)
        {
            CompanyInfo c = d.Company;
            string logo = Image(assetsDir, "logo.png");

            var pages = new List<string>
            {
                CoverPage(d, logo),
                LetterPage(d),
                GeneralPage(d),
                NetworkPage(d),
                FigurePage("6.1", "Figura A — Afastamentos mínimos",
                    "Afastamentos mínimos entre condutores da rede de telecomunicações e da rede de distribuição de energia elétrica ao longo do vão.",
                    Figure(assetsDir, "fig-a-afastamentos.png", "Figura A — Afastamentos mínimos ao longo do vão", 175)),
                PlatePage(d, assetsDir),
                FigurePage("6.3", "Espaçamento mínimo entre poste e reserva técnica",
                    "Reserva técnica de cabo de fibra óptica no meio do vão.",
                    Figure(assetsDir, "fig-c-reserva-tecnica.jpg", "Figura C — Reserva técnica no meio do vão", 175)),
                FigurePage("6.4", "Afastamento mínimo em alta tensão dupla",
                    "Distância de compartilhamento de alta tensão dupla em relação aos circuitos de baixa, média tensão e telecomunicações.",
                    Figure(assetsDir, "fig-d-alta-tensao.jpg", "Figura D — Alta tensão dupla", 175)),
                FigurePage("6.5", "Espaçamento mínimo em relação ao aterramento",
                    "Espaçamentos mínimos e aterramento dos equipamentos da ocupante nos postes.",
                    Figure(assetsDir, "fig-e-aterramento.jpg", "Figura E — Aterramento dos equipamentos", 112)) +
                    Section("7", "Endereço da obra", $"""<div class="card soft">{E(d.WorkAddress)}</div>""") +
                    Signature(c)
            };

            var html = new StringBuilder();
            html.Append($$"""
                <!DOCTYPE html>
                <html lang="pt-BR"><head><meta charset="utf-8"><title>Memorial Descritivo - {{E(d.Route)}}</title>
                <style>{{Css}}</style></head><body>
                """);
            for (int i = 0; i < pages.Count; i++)
            {
                // A capa não tem cabeçalho nem rodapé; as demais levam o número da página
                html.Append(i == 0 ? pages[i] : Page(pages[i], d, logo, i + 1, pages.Count));
            }
            html.Append("</body></html>");
            return html.ToString();
        }

        // ---------- Páginas ----------

        private static string CoverPage(MemorialData d, string logo)
        {
            CompanyInfo c = d.Company;
            string Fact(string label, string value, string? detail = null) => value.Length == 0 ? "" : $"""
                <div class="fact"><div class="l">{label}</div><div class="v">{E(value)}</div>{(detail != null ? $"""<div class="s">{E(detail)}</div>""" : "")}</div>
                """;

            return $$"""
                <section class="page cover">
                  <div class="topbar"></div>
                  <div class="cover-logo">{{(logo.Length > 0 ? $"""<img src="{logo}" alt="">""" : "")}}</div>
                  <div class="cover-title">
                    <div class="eyebrow">Projeto de compartilhamento de postes</div>
                    <h1>Memorial Descritivo</h1>
                    <div class="sub">Cálculo de esforço mecânico e ocupação de postes · Rede óptica FTTx</div>
                    <div class="route big">{{E(d.Route)}}</div>
                  </div>
                  <div class="facts">
                    {{Fact("Solicitante", c.LegalName, c.Cnpj.Length > 0 ? "CNPJ " + c.Cnpj : null)}}
                    {{Fact("Concessionária", c.Utility, c.Department)}}
                    {{Fact("Responsável técnico", c.Representative, c.Crea.Length > 0 ? "CREA " + c.Crea : null)}}
                    {{Fact("Local e data", d.PlaceAndDate)}}
                  </div>
                  <div class="band">
                    <svg class="waves" viewBox="0 0 210 64" preserveAspectRatio="none">
                      <path d="M-10 58 C 40 10, 120 70, 220 12" stroke="rgba(255,255,255,.10)" stroke-width="10" fill="none"/>
                      <path d="M-10 64 C 60 24, 130 80, 220 30" stroke="rgba(18,160,75,.55)" stroke-width="3" fill="none"/>
                      <path d="M-10 40 C 50 0, 140 46, 220 -4" stroke="rgba(255,255,255,.18)" stroke-width="1.2" fill="none"/>
                    </svg>
                    <div class="band-kpis">
                      <div><b>{{N(d.PoleCount)}}</b><span>postes</span></div>
                      <div><b>{{M(d.CableLength)}}</b><span>de cabo óptico</span></div>
                      <div><b>{{d.Cables.Count}}</b><span>tipo(s) de cabo</span></div>
                      {{(d.CtoCount + d.CeoCount > 0 ? $"<div><b>{d.CtoCount} / {d.CeoCount}</b><span>CTO / CEO</span></div>" : "")}}
                    </div>
                  </div>
                </section>
                """;
        }

        private static string LetterPage(MemorialData d)
        {
            CompanyInfo c = d.Company;

            // Qualificação da empresa e do representante; dados vazios ficam de fora
            var text = new StringBuilder($"A <b>{E(c.LegalName)}</b>");
            if (c.Address.Length > 0) text.Append($", com sede na <b>{E(Join(", ", c.Address, c.City))}</b>");
            if (c.Cnpj.Length > 0) text.Append($", devidamente inscrita no <b>CNPJ sob o nº {E(c.Cnpj)}</b>");
            if (c.Representative.Length > 0)
            {
                text.Append($", neste ato representada pelo Sr. <b>{E(c.Representative)}</b>");
                if (c.Qualification.Length > 0) text.Append($", {E(c.Qualification)}");
                if (c.Rg.Length > 0) text.Append($", portador do <b>RG nº {E(c.Rg)}</b>");
                if (c.Cpf.Length > 0) text.Append($" e do <b>CPF nº {E(c.Cpf)}</b>");
                if (c.RepresentativeAddress.Length > 0) text.Append($", residente e domiciliado à {E(c.RepresentativeAddress)}");
            }
            text.Append(", encaminha em anexo o <b>projeto de esforço mecânico para aprovação e ocupação de postes</b>");
            if (c.Crea.Length > 0) text.Append($", com registro no CREA/RO (Conselho Regional de Engenharia e Agronomia) sob o <b>nº {E(c.Crea)}</b>");
            text.Append(", e a descrição (data sheet) dos suprimentos empregados, com o objetivo apresentado neste memorial.");

            return $$"""
                <div class="date">{{E(d.PlaceAndDate)}}</div>
                <div class="to">
                  <div class="l">Destinatário</div>
                  <b>{{E(c.Utility)}}</b>
                  {{(c.Department.Length > 0 ? $"<div>{E(c.Department)}</div>" : "")}}
                  {{(c.Attention.Length > 0 ? $"<div>A/C {E(c.Attention)}</div>" : "")}}
                </div>
                <div class="subject"><span>Assunto</span>Projeto de ocupação de postes — {{E(d.Route)}}</div>
                <p class="greeting">Prezado Senhor,</p>
                <p class="letter">{{text}}</p>
                <p class="letter">Colocamo-nos à disposição para quaisquer esclarecimentos.</p>
                <p class="greeting">Atenciosamente,</p>
                {{Signature(c)}}
                """;
        }

        private static string GeneralPage(MemorialData d)
        {
            CompanyInfo c = d.Company;
            string Row(string label, string value) => value.Length == 0 ? "" : $"<dt>{label}</dt><dd>{E(value)}</dd>";

            string cables = d.Cables.Count == 0
                ? """<div class="card soft muted">Nenhum cabo lançado no desenho.</div>"""
                : "<ul class=\"cables\">" + string.Concat(d.Cables.Select(k =>
                    $"""<li><span class="dot"></span><b>Cabo óptico {E(k.Description)}</b>{(k.Model != null ? $"""<span class="muted"> · {E(k.Name)}</span>""" : "")}</li>""")) + "</ul>";

            return
                """<h1 class="doc-title">Memorial Descritivo</h1>""" +
                Section("1", "Dados gerais da empresa solicitante", $"""
                    <div class="card"><dl class="grid">
                      {Row("Razão social", c.LegalName)}
                      {Row("Endereço", Join(" – ", c.Address, c.City) + (c.ZipCode.Length > 0 ? " · CEP " + c.ZipCode : ""))}
                      {Row("CNPJ", c.Cnpj)}
                      {Row("Telefone", c.Phone)}
                      {Row("E-mail", c.Email)}
                    </dl></div>
                    """) +
                Section("2", "Objetivo", """
                    <p>Apresentação, para aprovação, do memorial descritivo e do cálculo de esforço mecânico de rede FTTx,
                    para o fim específico de compartilhamento e uso de postes na construção de rede de internet banda larga
                    e seus subprodutos.</p>
                    """) +
                Section("3", "Percurso da rede", $"""<div class="route">{E(d.Route)}</div>""") +
                Section("4", "Cabo óptico", cables) +
                Section("5", "Posteamento e cabos a serem utilizados", Kpis(d));
        }

        private static string NetworkPage(MemorialData d)
        {
            string poleRows = string.Concat(d.PoleTypes.Select(p =>
                $"""<tr><td><b>{E(p.Type)}</b></td><td>{E(p.Description)}</td><td class="num">{N(p.Count)}</td></tr>"""));
            string cableRows = string.Concat(d.Cables.Select(k =>
                $"""<tr><td><b>{E(k.Description)}</b></td><td>{E(k.Name)}</td><td class="num">{N(k.Runs)}</td><td class="num">{M(k.Length)}</td></tr>"""));

            return Section("5", "Posteamento e cabos a serem utilizados", $"""
                    <table>
                      <thead><tr><th>Poste</th><th>Descrição</th><th class="num">Quantidade</th></tr></thead>
                      <tbody>{poleRows}<tr class="total"><td colspan="2">Total de postes</td><td class="num">{N(d.PoleCount)}</td></tr></tbody>
                    </table>
                    <table class="gap">
                      <thead><tr><th>Cabo óptico projetado</th><th>Nome curto</th><th class="num">Lances</th><th class="num">Metragem</th></tr></thead>
                      <tbody>{cableRows}<tr class="total"><td colspan="3">Total de cabo óptico</td><td class="num">{M(d.CableLength)}</td></tr></tbody>
                    </table>
                    <div class="totals">
                      <div><span>Total de postes para pagar aluguel à {E(Short(d.Company.Utility))}</span><b>{N(d.PoleCount)}</b></div>
                      <div><span>Total de cabo óptico</span><b>{M(d.CableLength)}</b></div>
                    </div>
                    """) +
                Section("6", "Detalhamento de instalação", """
                    <p>Os cabos ópticos serão instalados de forma aérea, fixados aos postes, com o uso das ferragens adequadas,
                    essenciais para uma boa organização no lançamento. A ordem do número de fibras segue o definido no projeto.</p>
                    <p>As figuras a seguir apresentam os afastamentos e espaçamentos mínimos a serem respeitados na instalação.</p>
                    """);
        }

        private static string PlatePage(MemorialData d, string? assetsDir)
        {
            CompanyInfo c = d.Company;
            string plate = $"""
                <div class="plate">
                  <span class="hole l"></span><span class="hole r"></span>
                  <div class="t1">{E(c.PlateName.Length > 0 ? c.PlateName : c.LegalName)}</div>
                  <div class="t2">TIPO DE CABO: FIBRA ÓPTICA</div>
                  <div class="t2">ROTA: {E(d.Route.ToUpper(Br))}</div>
                  {(c.EmergencyPhone.Length > 0 ? $"""<div class="t2">EMERGÊNCIA: {E(c.EmergencyPhone)}</div>""" : "")}
                </div>
                """;

            return FigureTitle("6.2", "Figura B — Plaqueta de identificação de cabos",
                       "Plaqueta instalada no cabo, junto ao poste, com os dados da ocupante.") +
                   Figure(assetsDir, "fig-b-plaqueta.png", null, 88) + plate +
                   """
                   <div class="specs">
                     <div><span>Fundo</span><b>Amarelo</b></div>
                     <div><span>Letras</span><b>Pretas · 15 × 3 mm</b></div>
                     <div><span>Dimensões da placa</span><b>90 × 40 × 3 mm</b></div>
                     <div><span>Material</span><b>PVC acrílico</b></div>
                   </div>
                   <div class="callout"><b>Obs.:</b> é obrigatória a colocação de uma plaqueta de identificação, presa no cabo
                   com fio de espinar e fixada a 300 mm do poste.</div>
                   """;
        }

        private static string FigurePage(string number, string title, string description, string figure) =>
            FigureTitle(number, title, description) + figure;

        // ---------- Blocos ----------

        private static string Page(string body, MemorialData d, string logo, int number, int total)
        {
            CompanyInfo c = d.Company;
            return $$"""
                <section class="page">
                  <div class="topbar"></div>
                  <div class="header">
                    {{(logo.Length > 0 ? $"""<img src="{logo}" alt="">""" : $"<b>{E(c.LegalName)}</b>")}}
                    <div class="doc"><b>MEMORIAL DESCRITIVO</b>{{E(d.Route)}}</div>
                  </div>
                  <div class="body">{{body}}</div>
                  <div class="footer">
                    <span>{{E(Join(" · CNPJ ", c.LegalName, c.Cnpj))}}</span>
                    <span class="pg">Página {{number}} de {{total}}</span>
                  </div>
                </section>
                """;
        }

        private static string Section(string number, string title, string content) => $"""
            <section class="block">
              <h2><span class="n">{number}</span>{E(title)}</h2>
              {content}
            </section>
            """;

        private static string FigureTitle(string number, string title, string description) => $"""
            <div class="eyebrow">6 · Detalhamento de instalação</div>
            <h2 class="fig-title"><span class="tag">{number}</span>{E(title)}</h2>
            <p class="muted fig-desc">{E(description)}</p>
            """;

        private static string Kpis(MemorialData d)
        {
            var tiles = new List<string>
            {
                $"""<div class="kpi"><div class="v">{N(d.PoleCount)}</div><div class="l">Postes a ocupar</div></div>""",
                $"""<div class="kpi green"><div class="v">{M(d.CableLength)}</div><div class="l">Cabo óptico projetado</div></div>"""
            };
            if (d.CtoCount > 0) tiles.Add($"""<div class="kpi light"><div class="v">{N(d.CtoCount)}</div><div class="l">CTO</div></div>""");
            if (d.CeoCount > 0) tiles.Add($"""<div class="kpi light"><div class="v">{N(d.CeoCount)}</div><div class="l">CEO</div></div>""");
            return $"""
                <div class="kpis" style="grid-template-columns: repeat({tiles.Count}, 1fr)">{string.Concat(tiles)}</div>
                <p class="muted note">Quantidades levantadas do desenho do projeto. Detalhamento na página seguinte.</p>
                """;
        }

        private static string Signature(CompanyInfo c) => c.Representative.Length == 0 ? "" : $"""
            <div class="signature">
              <div class="line"></div>
              <b>{E(c.Representative)}</b>
              <div>{E(c.Crea.Length > 0 ? "Responsável técnico · CREA " + c.Crea : "Responsável técnico")}</div>
              <div class="muted">{E(c.LegalName)}</div>
            </div>
            """;

        /// <summary>Figura de Dados\Memorial embutida; sem o arquivo, um aviso no lugar.</summary>
        private static string Figure(string? assetsDir, string file, string? caption, int maxHeightMm)
        {
            string src = Image(assetsDir, file);
            string content = src.Length > 0
                ? $"""<img src="{src}" alt="" style="max-height:{maxHeightMm}mm">"""
                : $"""<div class="missing">Figura não encontrada: Dados/{AssetsFolder}/{E(file)}</div>""";
            return $"""<figure>{content}{(caption != null ? $"<figcaption>{E(caption)}</figcaption>" : "")}</figure>""";
        }

        /// <summary>Imagem como data URI (o HTML fica autossuficiente). Vazio se o arquivo não existir.</summary>
        private static string Image(string? assetsDir, string file)
        {
            string? path = assetsDir == null ? null : Path.Combine(assetsDir, file);
            if (path == null || !File.Exists(path)) return "";
            string mime = Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }

        // ---------- Formatação ----------

        private static string E(string text) => WebUtility.HtmlEncode(text);
        private static string N(int value) => value.ToString("N0", Br);
        private static string M(double meters) => Math.Round(meters).ToString("N0", Br) + " m";
        private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => p.Length > 0));

        /// <summary>"Energisa Distribuição Rondônia" → "Energisa" (para frases curtas).</summary>
        private static string Short(string utility) => utility.Length == 0 ? "concessionária" : utility.Split(' ')[0];

        // ---------- Estilo ----------

        private const string Css = """
            @page { size: A4; margin: 0; }
            * { box-sizing: border-box; }
            html, body { margin: 0; padding: 0; }
            body { font-family: "Segoe UI", Calibri, Arial, sans-serif; color: #1F2A37; font-size: 10.5pt; line-height: 1.55;
                   -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            b { font-weight: 600; }
            p { margin: 0 0 3mm; text-align: justify; }
            .muted { color: #6B7785; }

            .page { width: 210mm; height: 297mm; position: relative; overflow: hidden; break-after: page; background: #fff; }
            .page:last-child { break-after: auto; }
            .topbar { position: absolute; top: 0; left: 0; right: 0; height: 2.4mm;
                      background: linear-gradient(90deg, #0B4F75 0%, #1B6FA8 55%, #12A04B 100%); }
            .header { position: absolute; top: 8mm; left: 18mm; right: 18mm; height: 18mm; display: flex; align-items: center;
                      justify-content: space-between; border-bottom: .3mm solid #E3E8EE; }
            .header img { height: 12.5mm; }
            .header .doc { text-align: right; font-size: 8pt; color: #6B7785; line-height: 1.4; }
            .header .doc b { display: block; color: #0B4F75; font-size: 8.5pt; letter-spacing: .14em; }
            .body { position: absolute; top: 33mm; left: 18mm; right: 18mm; bottom: 20mm; }
            .footer { position: absolute; bottom: 8mm; left: 18mm; right: 18mm; display: flex; justify-content: space-between;
                      font-size: 7.5pt; color: #8A95A3; border-top: .3mm solid #E3E8EE; padding-top: 2.5mm; }
            .footer .pg { color: #0B4F75; font-weight: 600; }

            .doc-title { font-size: 20pt; color: #0B4F75; margin: 0 0 6mm; font-weight: 700; letter-spacing: .01em; }
            .doc-title::after { content: ""; display: block; width: 18mm; height: 1.2mm; margin-top: 2mm; border-radius: 1mm; background: #12A04B; }
            section.block { margin-bottom: 6.5mm; }
            h2 { display: flex; align-items: center; gap: 3mm; font-size: 12.5pt; color: #0B4F75; margin: 0 0 3mm; font-weight: 700; }
            h2 .n { display: inline-flex; align-items: center; justify-content: center; width: 7.5mm; height: 7.5mm; border-radius: 50%;
                    background: #12A04B; color: #fff; font-size: 9.5pt; flex: none; }
            .card { border: .3mm solid #E3E8EE; border-radius: 2.5mm; padding: 4mm 5mm; }
            .soft { background: #F4F7FA; border-color: #E6ECF2; }
            dl.grid { display: grid; grid-template-columns: 34mm 1fr; row-gap: 2.2mm; column-gap: 4mm; margin: 0; }
            dl.grid dt { color: #6B7785; font-size: 8pt; text-transform: uppercase; letter-spacing: .05em; padding-top: .5mm; }
            dl.grid dd { margin: 0; font-weight: 600; }
            .route { display: inline-block; background: #EAF6EF; color: #0B7A38; border: .3mm solid #BFE6CF; border-radius: 10mm;
                     padding: 1.8mm 6mm; font-weight: 700; font-size: 11pt; }
            .route.big { font-size: 14pt; margin-top: 8mm; padding: 2.5mm 8mm; }
            ul.cables { list-style: none; margin: 0; padding: 0; }
            ul.cables li { display: flex; align-items: center; gap: 3mm; padding: 2.5mm 4mm; border: .3mm solid #E3E8EE;
                           border-radius: 2mm; margin-bottom: 2mm; }
            ul.cables .dot { width: 3mm; height: 3mm; border-radius: 50%; background: #1B6FA8; box-shadow: 0 0 0 1mm #DCEBF6; flex: none; }
            .kpis { display: grid; gap: 4mm; }
            .kpi { border-radius: 3mm; padding: 4.5mm 5mm; background: #0B4F75; color: #fff; }
            .kpi.green { background: #12A04B; }
            .kpi.light { background: #F4F7FA; color: #0B4F75; border: .3mm solid #E3E8EE; }
            .kpi .v { font-size: 19pt; font-weight: 700; line-height: 1.15; }
            .kpi .l { font-size: 7.5pt; text-transform: uppercase; letter-spacing: .08em; opacity: .85; margin-top: 1mm; }
            .note { font-size: 8.5pt; margin-top: 3mm; }

            table { width: 100%; border-collapse: collapse; font-size: 9.5pt; }
            table.gap { margin-top: 5mm; }
            thead tr { background: #0B4F75; }
            th { color: #fff; text-align: left; padding: 2.4mm 3.5mm; font-weight: 600; font-size: 7.8pt;
                 text-transform: uppercase; letter-spacing: .06em; }
            td { padding: 2.3mm 3.5mm; border-bottom: .25mm solid #E3E8EE; }
            tbody tr:nth-child(even) { background: #F7F9FB; }
            .num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
            tbody tr.total { background: #EAF6EF; } tr.total td { font-weight: 700; color: #0B4F75; border-bottom: none; }
            .totals { display: grid; grid-template-columns: 1fr 1fr; gap: 4mm; margin-top: 5mm; }
            .totals div { border-left: 1.2mm solid #12A04B; background: #F4F7FA; border-radius: 0 2mm 2mm 0; padding: 3mm 4mm; }
            .totals span { display: block; font-size: 8pt; color: #6B7785; text-transform: uppercase; letter-spacing: .04em; }
            .totals b { font-size: 15pt; color: #0B4F75; }

            .eyebrow { color: #12A04B; font-size: 8pt; font-weight: 700; letter-spacing: .2em; text-transform: uppercase; margin-bottom: 2mm; }
            .fig-title .tag { background: #0B4F75; color: #fff; border-radius: 1.5mm; padding: .8mm 2.5mm; font-size: 9.5pt; }
            .fig-desc { margin-bottom: 5mm; }
            figure { margin: 0 0 6mm; border: .3mm solid #E3E8EE; border-radius: 3mm; padding: 6mm; text-align: center; }
            figure img { max-width: 100%; }
            figcaption { margin-top: 3mm; font-size: 8.5pt; color: #6B7785; }
            .missing { padding: 20mm 0; color: #B4232C; }

            .plate { width: 112mm; margin: 0 auto 6mm; background: #FFE500; border: .5mm solid #1A1A1A; border-radius: 1.5mm;
                     padding: 7mm 6mm 6mm; text-align: center; position: relative; color: #111; font-weight: 700; line-height: 1.5; }
            .plate .hole { position: absolute; top: 2.5mm; width: 4.5mm; height: 4.5mm; border-radius: 50%; background: #fff; border: .3mm solid #333; }
            .plate .hole.l { left: 3mm; } .plate .hole.r { right: 3mm; }
            .plate .t1 { font-size: 15pt; margin-bottom: 1.5mm; }
            .plate .t2 { font-size: 9pt; }
            .specs { display: grid; grid-template-columns: repeat(4, 1fr); gap: 3mm; margin-bottom: 5mm; }
            .specs div { border: .3mm solid #E3E8EE; border-radius: 2mm; padding: 2.5mm 3mm; }
            .specs span { display: block; font-size: 7.5pt; color: #6B7785; text-transform: uppercase; letter-spacing: .04em; }
            .callout { border-left: 1.2mm solid #12A04B; background: #F4F7FA; padding: 3mm 4mm; border-radius: 0 2mm 2mm 0; font-size: 9.5pt; }

            .date { text-align: right; color: #6B7785; margin: 2mm 0 8mm; }
            .to { border-left: 1.2mm solid #12A04B; background: #F4F7FA; border-radius: 0 2mm 2mm 0; padding: 4mm 5mm; line-height: 1.6; }
            .to .l, .subject span { display: block; font-size: 7.5pt; color: #6B7785; text-transform: uppercase; letter-spacing: .08em; }
            .to b { color: #0B4F75; font-size: 11.5pt; }
            .subject { margin: 6mm 0 8mm; font-weight: 600; color: #0B4F75; }
            .greeting { margin: 0 0 4mm; }
            .letter { line-height: 1.75; margin-bottom: 4mm; }
            .signature { width: 110mm; margin: 12mm auto 0; text-align: center; line-height: 1.5; }
            .signature .line { border-top: .3mm solid #1F2A37; margin-bottom: 2mm; }
            .signature b { color: #0B4F75; font-size: 11pt; }

            .cover .cover-logo { position: absolute; top: 24mm; left: 0; right: 0; text-align: center; }
            .cover .cover-logo img { height: 40mm; }
            .cover .cover-title { position: absolute; top: 84mm; left: 18mm; right: 18mm; text-align: center; }
            .cover .eyebrow { font-size: 9pt; letter-spacing: .28em; }
            .cover h1 { font-size: 34pt; color: #0B4F75; margin: 2mm 0 1mm; font-weight: 700; letter-spacing: .01em; line-height: 1.15; }
            .cover .sub { color: #6B7785; font-size: 11.5pt; }
            .cover .facts { position: absolute; top: 150mm; left: 18mm; right: 18mm; display: grid; grid-template-columns: 1fr 1fr; gap: 4mm; }
            .cover .fact { border: .3mm solid #E3E8EE; border-radius: 3mm; padding: 4mm 5mm; }
            .cover .fact .l { font-size: 7.5pt; color: #12A04B; font-weight: 700; text-transform: uppercase; letter-spacing: .1em; }
            .cover .fact .v { font-weight: 700; color: #0B4F75; font-size: 10.5pt; margin-top: 1mm; line-height: 1.35; }
            .cover .fact .s { font-size: 8.5pt; color: #6B7785; margin-top: .5mm; }
            .cover .band { position: absolute; left: 0; right: 0; bottom: 0; height: 64mm; color: #fff;
                           background: linear-gradient(115deg, #0B4F75 0%, #0E5E8C 55%, #0F7C5C 100%); }
            .cover .waves { position: absolute; inset: 0; width: 100%; height: 100%; }
            .cover .band-kpis { position: absolute; left: 18mm; right: 18mm; bottom: 16mm; display: flex; justify-content: space-around; text-align: center; }
            .cover .band-kpis b { display: block; font-size: 22pt; line-height: 1.1; }
            .cover .band-kpis span { font-size: 8pt; text-transform: uppercase; letter-spacing: .12em; opacity: .85; }
            """;
    }
}
