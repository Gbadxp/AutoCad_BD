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
            ("Postes e Blocos", new[]
            {
                new Tool("FIBRA_INSERIR_POSTE", Theme.Icons.Pin, "Inserir Postes", "Numeração sequencial automática", "Inserir\nPostes"),
                new Tool("FIBRA_INSERIR_BLOCO", Theme.Icons.Blocks, "Inserir Blocos", "CTO, CEO e blocos da biblioteca", "Inserir\nBlocos"),
                new Tool("FIBRA_NOMEAR_POSTE", Theme.Icons.Tag, "Nomear Postes", "Numeração, 11/300 e coordenada UTM", "Nomear Postes", largeOnRibbon: false),
                new Tool("FIBRA_LISTA_POSTES", Theme.Icons.Page, "Listagem de Postes", "Postes por tipo DT/CC, altura e esforço (.csv)", "Listagem de Postes", largeOnRibbon: false),
                new Tool("FIBRA_NUMERAR_PONTOS", Theme.Icons.List, "Numerar Pontos", "P01, P02... com coordenadas X/Y", "Numerar Pontos", largeOnRibbon: false)
            }),
            ("Esforços", new[]
            {
                new Tool("FIBRA_ESFORCO_TOTAL", Theme.Icons.Bolt, "Esforço Total no Poste", "Soma todos os cabos do poste clicado", "Esforço\nno Poste"),
                new Tool("FIBRA_ESFORCO_PERCURSO", Theme.Icons.Path, "Esforço no Percurso", "Setas em todos os postes de um cabo", "Esforço no\nPercurso"),
                new Tool("FIBRA_RELATORIO_ESFORCOS", Theme.Icons.Document, "Relatório de Esforços", "CSV com a situação OK / EXCEDIDO", "Relatório de\nEsforços")
            }),
            ("Materiais e Rede", new[]
            {
                new Tool("FIBRA_EXPORTAR_CSV", Theme.Icons.Export, "Lista de Materiais", "Blocos, metragem e coordenadas (.csv)", "Lista de\nMateriais"),
                new Tool("FIBRA_CALCULAR_BOBINAS", Theme.Icons.Calculator, "Calcular Bobinas", "Quantidade de bobinas por tipo de cabo", "Calcular\nBobinas"),
                new Tool("FIBRA_BUDGET_OPTICO", Theme.Icons.Signal, "Budget Óptico", "Perdas do enlace GPON (B+ / C+)", "Budget\nÓptico")
            }),
            ("Pranchas", new[]
            {
                new Tool("FIBRA_GERAR_FOLHAS", Theme.Icons.Sheets, "Gerar Folhas", "Divide a área em folhas A0 a A4 com viewports", "Gerar\nFolhas")
            }),
            ("Fiber Plugin", new[]
            {
                new Tool("FIBRA_ESCALA", Theme.Icons.Ruler, "Escala do Desenho", "Tamanho dos textos: 1:500, 1:1000, 1:2000...", "Escala", largeOnRibbon: false),
                new Tool("FIBRA_EXPORTAR_BLOCOS", Theme.Icons.Library, "Exportar Blocos", "Copia os blocos do desenho para o BLOCOS.dwg", "Exportar Blocos", largeOnRibbon: false),
                new Tool("FIBRA_ABRIR_PASTA", Theme.Icons.Folder, "Pasta de Dados", "Planilha de cabos e biblioteca de blocos", "Pasta de Dados", largeOnRibbon: false)
            })
        };
    }
}
