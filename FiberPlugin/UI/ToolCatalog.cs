namespace FiberPlugin.UI
{
    internal sealed class Tool
    {
        public Tool(string command, string glyph, string title, string description, string ribbonLabel, bool largeOnRibbon = true)
        {
            Command = command;
            Glyph = glyph;
            Title = title;
            Description = description;
            RibbonLabel = ribbonLabel;
            LargeOnRibbon = largeOnRibbon;
        }

        public string Command { get; }
        public string Glyph { get; }
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
                new Tool("FIBRA_LANCAR_CABO", Theme.Icons.Edit, "Lançar Rota Manual", "Desenhe o cabo clicando nos postes", "Lançar\nCabo"),
                new Tool("FIBRA_ROTEAMENTO_AUTO", Theme.Icons.Route, "Roteamento Automático", "Rota mais curta entre os blocos", "Roteamento\nAutomático")
            }),
            ("Inserir", new[]
            {
                new Tool("FIBRA_INSERIR_POSTE", Theme.Icons.Pin, "Inserir Postes", "DT/CC já com número, 11/300 e UTM", "Postes", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_CTO", Theme.Icons.Blocks, "Inserir CTO", "CTO-01, CTO-02... com UTM e poste vinculado", "CTO", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_CEO", Theme.Icons.Blocks, "Inserir CEO", "CEO-01, CEO-02... com UTM e poste vinculado", "CEO", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_ELETRICOS", Theme.Icons.Bolt, "Itens Elétricos", "Trafo, chaves, para-raio, aterramento", "Elétricos", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_AMARRACAO", Theme.Icons.Pin, "Amarração", "Amarração com direção", "Amarração", largeOnRibbon: false),
                new Tool("FIBRA_INSERIR_BLOCO", Theme.Icons.Blocks, "Outros Blocos", "Blocos do BLOCOS.dwg fora dos grupos", "Outros Blocos", largeOnRibbon: false),
                new Tool("FIBRA_RENUMERAR", Theme.Icons.Edit, "Renumerar", "Clique nos blocos na ordem para corrigir a numeração", "Renumerar", largeOnRibbon: false)
            }),
            ("Esforços", new[]
            {
                new Tool("FIBRA_ESFORCO_TOTAL", Theme.Icons.Bolt, "Esforço Total no Poste", "Soma todos os cabos do poste clicado", "Esforço\nno Poste"),
                new Tool("FIBRA_ESFORCO_PERCURSO", Theme.Icons.Path, "Esforço no Percurso", "Setas em todos os postes de um cabo", "Esforço no\nPercurso")
            }),
            ("Relatório", new[]
            {
                new Tool("FIBRA_RELATORIO", Theme.Icons.Document, "Gerar Relatório", "Excel com postes, esforços e cabos do desenho", "Gerar\nRelatório")
            }),
            ("Pranchas", new[]
            {
                new Tool("FIBRA_GERAR_FOLHAS", Theme.Icons.Sheets, "Gerar Folhas", "Divide a área em folhas A0 a A4 com viewports", "Gerar\nFolhas")
            }),
            ("Fiber Plugin", new[]
            {
                new Tool("FIBRA_ZONA_UTM", Theme.Icons.Ruler, "Zona UTM", "Zona e hemisfério das coordenadas (ex.: 20 L)", "Zona UTM", largeOnRibbon: false),
                new Tool("FIBRA_ESCALA", Theme.Icons.Ruler, "Escala do Desenho", "Tamanho dos textos: 1:500, 1:1000, 1:2000...", "Escala", largeOnRibbon: false),
                new Tool("FIBRA_ABRIR_PASTA", Theme.Icons.Folder, "Pasta de Dados", "Planilha de cabos e biblioteca de blocos", "Pasta de Dados", largeOnRibbon: false)
            })
        };
    }
}
