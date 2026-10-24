namespace FiberPlugin.UI
{
    internal sealed class Tool
    {
        public Tool(string command, string title, string description, string ribbonLabel, bool largeOnRibbon = true, string? ribbonMenu = null)
        {
            Command = command;
            Title = title;
            Description = description;
            RibbonLabel = ribbonLabel;
            LargeOnRibbon = largeOnRibbon;
            RibbonMenu = ribbonMenu;
        }

        public string Command { get; }
        public string Title { get; }
        public string Description { get; }

        /// <summary>Texto do botão na faixa de opções ("\n" quebra a linha nos botões grandes).</summary>
        public string RibbonLabel { get; }
        public bool LargeOnRibbon { get; }

        /// <summary>
        /// Botão de lista da aba Fibra em que a ferramenta fica (null = botão próprio): as do mesmo nome viram um botão
        /// só, que abre a lista delas com ícone e descrição. No menu principal (FIBRA) elas continuam como as outras.
        /// </summary>
        public string? RibbonMenu { get; }
    }

    /// <summary>
    /// Lista única das ferramentas do plugin, usada pelo menu principal (FIBRA) e pela aba "Fibra".
    /// Para incluir um comando novo nos dois lugares, basta acrescentá-lo aqui.
    /// O "Esforço 1 Cabo" (FIBRA_CALCULAR_ESFORCO) fica fora de propósito.
    /// </summary>
    internal static class ToolCatalog
    {
        public const string MenuCommand = "FIBRA";

        /// <summary>Comandos sem botão na faixa de opções, que também podem ter atalho (aba Atalhos de Configurações).</summary>
        private static readonly (string Command, string Title)[] CommandLineOnly =
        {
            ("FIBRA_CALCULAR_ESFORCO", "Esforço de 1 Cabo"),
            ("FIBRA_ID_ENERGISA", "ID Energisa dos Postes"),
            ("FIBRA_ZONA_UTM", "Zona UTM"),
            ("FIBRA_ATUALIZAR_COORDENADAS", "Atualizar Coordenadas"),
            ("FIBRA_ESCALA", "Escala do Desenho"),
            ("FIBRA_ABRIR_PASTA", "Pasta de Dados"),
            ("FIBRA_ATUALIZAR_BLOCOS", "Atualizar Blocos"),
            ("FIBRA_EXPORTAR_BLOCOS", "Exportar Blocos para o BLOCOS.dwg"),
            ("FIBRA_SOBRE", "Sobre o Fiber Plugin"),
            ("FIBRA_RIBBON", "Recriar a Aba Fibra")
        };

        /// <summary>Todos os comandos que podem ter atalho, na ordem da aba Atalhos: o menu, os botões e os de linha de comando.</summary>
        public static IEnumerable<(string Command, string Title)> AllCommands =>
            new[] { (MenuCommand, "Menu do Fiber Plugin") }
                .Concat(Sections.SelectMany(s => s.Tools).Select(t => (t.Command, t.Title)))
                .Concat(CommandLineOnly);

        private const string LaunchMenu = "Lançar";

        /// <summary>
        /// Botões de lista divididos: o clique no botão roda a primeira ferramenta (com o nome e o ícone dela) e só a setinha
        /// abre a lista, como os botões divididos do AutoCAD. Nos outros, o botão inteiro abre a lista.
        /// </summary>
        public static readonly HashSet<string> SplitMenus = new HashSet<string> { LaunchMenu };

        /// <summary>Chave do ícone do botão de lista (Tool.RibbonMenu).</summary>
        public static string MenuIcon(string menu) => "FIBRA_GRUPO_" + menu.ToUpperInvariant();

        public static readonly (string Section, Tool[] Tools)[] Sections =
        {
            ("Cabos", new[]
            {
                new Tool("FIBRA_LANCAR_CABO", "Lançar Cabo", "Desenhe o cabo clicando nos postes", "Lançar\nCabo", ribbonMenu: LaunchMenu),
                new Tool("FIBRA_LANCAR_BACKBONE", "Lançar Backbone",
                    "Cabo tronco: como o Lançar Cabo, com a linha mais grossa, na layer FIBRA_BACKBONE_<cabo>", "Lançar\nBackbone",
                    ribbonMenu: LaunchMenu),
                new Tool("FIBRA_ROTEAMENTO_AUTO", "Roteamento Automático", "Rota mais curta entre os blocos", "Roteamento\nAutomático"),
                new Tool("FIBRA_TROCAR_CABO", "Trocar Cabo", "Troca o tipo do cabo clicado (ex.: 06F.O por 12F.O) sem desenhar de novo", "Trocar Cabo", largeOnRibbon: false),
                new Tool("FIBRA_RESERVA_TECNICA", "Reserva Técnica", "Seta com \"RESERVA TÉCNICA 30m\" apontando para o poste ou o vão", "Reserva Técnica", largeOnRibbon: false),
                new Tool("FIBRA_CORDOALHA", "Lançar Cordoalha", "Linha laranja tracejada clicando nos postes, com a metragem de cada trecho", "Cordoalha", largeOnRibbon: false)
            }),
            ("Inserir", new[]
            {
                new Tool("FIBRA_INSERIR_POSTE", "Inserir Postes", "DT/CC já com número, 11/300 e UTM", "Postes", largeOnRibbon: false),
                new Tool("FIBRA_TROCAR_POSTE", "Trocar Poste", "Troca o modelo do poste clicado (ex.: 9/200 por 11/300, ou DT por CC) sem inserir de novo",
                    "Trocar Poste", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_CTO", "Inserir CTO", "CTO-01, CTO-02... ligadas ao poste mais próximo", "CTO", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_CEO", "Inserir CEO", "CEO-01, CEO-02... ligadas ao poste mais próximo", "CEO", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_ELETRICOS", "Itens Elétricos", "Trafo, chaves, para-raio, aterramento", "Elétricos", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_AMARRACAO", "Amarração", "Uma de cada lado do cabo nos postes selecionados (fim de rede: uma)", "Amarração", largeOnRibbon: false)
            }),
            ("Esforços", new[]
            {
                new Tool("FIBRA_ESFORCO_TOTAL", "Esforço Total no Poste", "Soma todos os cabos do poste clicado", "Esforço\nno Poste"),
                new Tool("FIBRA_ESFORCO_PERCURSO", "Esforço no Percurso", "Esforço em todos os postes do cabo; seta onde a NDU 009 pede", "Esforço no\nPercurso"),
                new Tool("FIBRA_PARAMETROS", "Parâmetros de Cálculo", "Altura do cabo no poste e tração (Tabela 08 da NDU 009)", "Parâmetros", largeOnRibbon: false),
                new Tool("FIBRA_ESFORCO_EXISTENTE", "Esforço Existente", "Esforço que já existe no poste (Energisa e outras ocupantes)", "Esforço Existente", largeOnRibbon: false)
            }),
            // Numeração e tamanho dos blocos já inseridos: comandos à parte, não de inserir
            ("Editar", new[]
            {
                new Tool("FIBRA_RENUMERAR", "Renumerar", "Clique nos blocos na ordem para corrigir a numeração", "Renumerar", largeOnRibbon: false),
                new Tool("FIBRA_NUMERAR_AUTO", "Numeração Automática", "Clique no primeiro poste: a numeração segue os cabos (e as CTO/CEO junto)", "Numerar Auto", largeOnRibbon: false),
                new Tool("FIBRA_NUMERAR_MANUAL", "Numerar Clicando", "Digite o número inicial e clique nos postes na ordem: cada clique recebe o próximo", "Numerar Clicando", largeOnRibbon: false),
                new Tool("FIBRA_TAMANHO_BLOCO", "Tamanho do Bloco", "Muda o tamanho dos blocos selecionados", "Tamanho", largeOnRibbon: false),
                new Tool("FIBRA_PADRONIZAR_TAMANHOS", "Padronizar Tamanhos",
                    "Deixa postes, CTO/CEO, textos e setas no tamanho da escala do desenho (ex.: o que foi colado de outro desenho)",
                    "Padronizar", largeOnRibbon: false)
            }),
            ("Google Earth", new[]
            {
                new Tool("FIBRA_IMPORTAR_KML", "Importar KML", "Pontos, linhas e polígonos de um KML/KMZ para o desenho", "Importar\nKML", ribbonMenu: "KML"),
                new Tool("FIBRA_EXPORTAR_KML", "Exportar KML", "Postes, CTO/CEO e cabos do projeto para o Google Earth", "Exportar\nKML", ribbonMenu: "KML"),
                new Tool("FIBRA_IMPORTAR_RUAS", "Importar Ruas", "Ruas do OpenStreetMap na posição UTM do projeto", "Importar\nRuas")
            }),
            ("Pranchas", new[]
            {
                new Tool("FIBRA_GERAR_FOLHAS", "Gerar Folhas", "Divide a área em folhas A0 a A4 com viewports", "Gerar\nFolhas")
            }),
            ("Documentos", new[]
            {
                new Tool("FIBRA_VERIFICAR", "Verificar Projeto", "Confere o desenho com a NDU 009 e marca o que falta", "Verificar\nProjeto", ribbonMenu: "Documentos"),
                new Tool("FIBRA_RELATORIO", "Gerar Relatório", "Excel com postes, esforços e cabos do desenho", "Gerar\nRelatório", ribbonMenu: "Documentos"),
                new Tool("FIBRA_MEMORIAL", "Memorial Descritivo", "PDF do memorial para a concessionária, com os dados do projeto", "Memorial\nDescritivo", ribbonMenu: "Documentos"),
                new Tool("FIBRA_MEMORIAL_ESFORCO", "Memorial de Esforço", "PDF só com o cálculo de esforço mecânico dos postes", "Memorial de Esforço", ribbonMenu: "Documentos"),
                new Tool("FIBRA_COORDENADAS_POSTES", "Coordenadas dos Postes", "PDF com os postes e as coordenadas UTM", "Coordenadas dos Postes", ribbonMenu: "Documentos"),
                new Tool("FIBRA_NORMA", "Norma NDU 009", "Abre o PDF da NDU 009 da Energisa, que vem com o plugin", "Norma NDU 009", ribbonMenu: "Documentos"),
                new Tool("FIBRA_PRODUCAO", "Produção do Dia", "Metros de cabo, CTO, CEO e postes feitos no dia, em todos os desenhos", "Produção do Dia", ribbonMenu: "Documentos")
            }),
            // Um botão só, "Configuração", com as duas: a de projeto (os dados deste projeto e da empresa: percurso, contrato,
            // ART, zona UTM, escala, pasta de dados e Atualizar Blocos) e a de desenho (o plugin e o que ele põe no desenho,
            // que vale para todos os desenhos)
            ("Ajustes", new[]
            {
                new Tool("FIBRA_DADOS_PROJETO", "Configuração de Projeto", "Nome do projeto, contrato, ART, zona UTM, escala e dados da empresa",
                    "Projeto", ribbonMenu: "Configuração"),
                new Tool("FIBRA_CONFIGURACOES", "Configuração de Desenho",
                    "Cabos e cores, postes, tração, nomes (P-01, CTO-01), tamanhos, cores do desenho e atalhos", "Configurações",
                    ribbonMenu: "Configuração")
            })
        };
    }
}
