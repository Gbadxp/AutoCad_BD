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
                new Tool("FIBRA_TAMANHO_BLOCO", "Tamanho do Bloco", "Muda o tamanho dos blocos selecionados", "Tamanho", largeOnRibbon: false)
            }),
            ("Esforços", new[]
            {
                new Tool("FIBRA_ESFORCO_TOTAL", "Esforço Total no Poste", "Soma todos os cabos do poste clicado", "Esforço\nno Poste"),
                new Tool("FIBRA_ESFORCO_PERCURSO", "Esforço no Percurso", "Setas em todos os postes de um cabo", "Esforço no\nPercurso"),
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
                new Tool("FIBRA_DADOS_PROJETO", "Dados do Projeto", "Percurso, contrato, ART... salvos para os documentos já abrirem preenchidos", "Dados do Projeto", largeOnRibbon: false)
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
            ("Fiber Plugin", new[]
            {
                new Tool("FIBRA_ZONA_UTM", "Zona UTM", "Zona e hemisfério das coordenadas (ex.: 20 L)", "Zona UTM", largeOnRibbon: false),
                new Tool("FIBRA_ESCALA", "Escala do Desenho", "Tamanho dos textos: 1:500, 1:1000, 1:2000...", "Escala", largeOnRibbon: false),
                new Tool("FIBRA_ABRIR_PASTA", "Pasta de Dados", "Planilha de cabos e biblioteca de blocos", "Pasta de Dados", largeOnRibbon: false),
                new Tool("FIBRA_ATUALIZAR_BLOCOS", "Atualizar Blocos", "Lê os blocos novos do seu BLOCOS.dwg, sem reinstalar", "Atualizar Blocos", largeOnRibbon: false)
            })
        };
    }
}
