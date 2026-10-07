namespace FiberPlugin.UI
{
    internal sealed class Tool
    {
        public Tool(string command, string title, string description, string ribbonLabel, bool largeOnRibbon = true)
        {
            Command = command;
            Title = title;
            Description = description;
            RibbonLabel = ribbonLabel;
            LargeOnRibbon = largeOnRibbon;
        }

        public string Command { get; }
        public string Title { get; }
        public string Description { get; }

        /// <summary>Texto do botão na faixa de opções ("\n" quebra a linha nos botões grandes).</summary>
        public string RibbonLabel { get; }
        public bool LargeOnRibbon { get; }
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

        public static readonly (string Section, Tool[] Tools)[] Sections =
        {
            ("Cabos", new[]
            {
                new Tool("FIBRA_LANCAR_CABO", "Lançar Rota Manual", "Desenhe o cabo clicando nos postes", "Lançar\nCabo"),
                new Tool("FIBRA_ROTEAMENTO_AUTO", "Roteamento Automático", "Rota mais curta entre os blocos", "Roteamento\nAutomático")
            }),
            ("Inserir", new[]
            {
                new Tool("FIBRA_INSERIR_POSTE", "Inserir Postes", "DT/CC já com número, 11/300 e UTM", "Postes", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_CTO", "Inserir CTO", "CTO-01, CTO-02... ligadas ao poste mais próximo", "CTO", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_CEO", "Inserir CEO", "CEO-01, CEO-02... ligadas ao poste mais próximo", "CEO", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_ELETRICOS", "Itens Elétricos", "Trafo, chaves, para-raio, aterramento", "Elétricos", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_AMARRACAO", "Amarração", "Uma de cada lado do cabo nos postes selecionados (fim de rede: uma)", "Amarração", largeOnRibbon: false),
                new Tool("FIBRA_RENUMERAR", "Renumerar", "Clique nos blocos na ordem para corrigir a numeração", "Renumerar", largeOnRibbon: false),
                new Tool("FIBRA_NUMERAR_AUTO", "Numeração Automática", "Clique no primeiro poste: a numeração segue os cabos (e as CTO/CEO junto)", "Numerar Auto", largeOnRibbon: false),
                new Tool("FIBRA_NUMERAR_MANUAL", "Numerar Clicando", "Digite o número inicial e clique nos postes na ordem: cada clique recebe o próximo", "Numerar Clicando", largeOnRibbon: false),
                new Tool("FIBRA_TAMANHO_BLOCO", "Tamanho do Bloco", "Muda o tamanho dos blocos selecionados", "Tamanho", largeOnRibbon: false)
            }),
            ("Esforços", new[]
            {
                new Tool("FIBRA_ESFORCO_TOTAL", "Esforço Total no Poste", "Soma todos os cabos do poste clicado", "Esforço\nno Poste"),
                new Tool("FIBRA_ESFORCO_PERCURSO", "Esforço no Percurso", "Esforço em todos os postes do cabo; seta onde a NDU 009 pede", "Esforço no\nPercurso"),
                new Tool("FIBRA_PARAMETROS", "Parâmetros de Cálculo", "Altura do cabo no poste e tração (Tabela 08 da NDU 009)", "Parâmetros", largeOnRibbon: false),
                new Tool("FIBRA_ESFORCO_EXISTENTE", "Esforço Existente", "Esforço que já existe no poste (Energisa e outras ocupantes)", "Esforço Existente", largeOnRibbon: false)
            }),
            ("Documentos", new[]
            {
                new Tool("FIBRA_VERIFICAR", "Verificar Projeto", "Confere o desenho com a NDU 009 e marca o que falta", "Verificar\nProjeto"),
                new Tool("FIBRA_RELATORIO", "Gerar Relatório", "Excel com postes, esforços e cabos do desenho", "Gerar\nRelatório"),
                new Tool("FIBRA_MEMORIAL", "Memorial Descritivo", "PDF do memorial para a concessionária, com os dados do projeto", "Memorial\nDescritivo"),
                new Tool("FIBRA_MEMORIAL_ESFORCO", "Memorial de Esforço", "PDF só com o cálculo de esforço mecânico dos postes", "Memorial de Esforço", largeOnRibbon: false),
                new Tool("FIBRA_COORDENADAS_POSTES", "Coordenadas dos Postes", "PDF com os postes e as coordenadas UTM", "Coordenadas dos Postes", largeOnRibbon: false),
                new Tool("FIBRA_NORMA", "Norma NDU 009", "Abre o PDF da NDU 009 da Energisa, que vem com o plugin", "Norma NDU 009", largeOnRibbon: false)
            }),
            ("Pranchas", new[]
            {
                new Tool("FIBRA_GERAR_FOLHAS", "Gerar Folhas", "Divide a área em folhas A0 a A4 com viewports", "Gerar\nFolhas")
            }),
            ("Google Earth", new[]
            {
                new Tool("FIBRA_IMPORTAR_KML", "Importar KML", "Pontos, linhas e polígonos de um KML/KMZ para o desenho", "Importar\nKML"),
                new Tool("FIBRA_EXPORTAR_KML", "Exportar KML", "Postes, CTO/CEO e cabos do projeto para o Google Earth", "Exportar\nKML"),
                new Tool("FIBRA_IMPORTAR_RUAS", "Importar Ruas", "Ruas do OpenStreetMap na posição UTM do projeto", "Importar\nRuas")
            }),
            // Dados do projeto (percurso, contrato, ART, zona UTM, escala, pasta de dados e Atualizar Blocos) ficam na
            // aba Projeto da janela Configurações
            ("Projeto", new[]
            {
                new Tool("FIBRA_CONFIGURACOES", "Configurações",
                    "Dados do projeto, cabos e cores, postes, tração, empresa, nomes (P-01, CTO-01) e tamanhos", "Configurações")
            })
        };
    }
}
