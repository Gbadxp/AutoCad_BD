namespace FiberPlugin.UI
{
    /// <summary>
    /// Passo a passo dos comandos de desenho e edição, mostrado na dica grande dos botões da aba Fibra junto com a animação
    /// (Dados\Animacoes\&lt;COMANDO&gt;.mp4, gerada pelo Instalacao\Animacoes). Um passo por linha.
    /// </summary>
    internal static class ToolHelp
    {
        private static readonly Dictionary<string, string[]> Steps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FIBRA_LANCAR_CABO"] = new[]
            {
                "Escolha o cabo na lista.",
                "Clique no primeiro poste e nos seguintes, na ordem: o trecho aparece a cada clique (Desfazer volta o último).",
                "Enter termina: o cabo entra com o nome e a metragem de cada vão."
            },
            ["FIBRA_LANCAR_BACKBONE"] = new[]
            {
                "Escolha o cabo tronco na lista.",
                "Clique nos postes, como no Lançar Cabo.",
                "Enter termina: a linha sai mais grossa, na layer FIBRA_BACKBONE_<cabo>."
            },
            ["FIBRA_ROTEAMENTO_AUTO"] = new[]
            {
                "Escolha o cabo e o lado dos postes por onde ele passa (Cima, Baixo, Esquerda ou Direita).",
                "Selecione os postes com uma janela.",
                "O plugin liga todos pela rota mais curta, afastada dos postes, com nome e metragem."
            },
            ["FIBRA_RESERVA_TECNICA"] = new[]
            {
                "Digite a metragem da reserva (Enter = a última usada).",
                "Clique no poste ou no cabo da reserva.",
                "Mova o mouse para o lado do texto e clique: entram a seta e \"RESERVA TÉCNICA 30m\"."
            },
            ["FIBRA_CORDOALHA"] = new[]
            {
                "Clique nos postes, na ordem, como no Lançar Cabo.",
                "Enter termina: linha laranja tracejada com a metragem de cada trecho."
            },
            ["FIBRA_INSERIR_POSTE"] = new[]
            {
                "Escolha o modelo (DT ou CC, altura e esforço) e o número do primeiro poste.",
                "Clique onde vai o poste; o DT gira com o mouse até o segundo clique.",
                "Cada poste entra com o texto (número, 11/300 e coordenadas). Modelo e Numero trocam no meio."
            },
            ["FIBRA_INSERIR_CTO"] = new[]
            {
                "Confirme o número da primeira CTO (Enter aceita o sugerido).",
                "Clique ao lado do poste: a CTO entra numerada (CTO-01, CTO-02...) e ligada ao poste mais próximo.",
                "Modelo troca o bloco e Numero muda a sequência no meio; Enter termina."
            },
            ["FIBRA_INSERIR_CEO"] = new[]
            {
                "Confirme o número da primeira CEO (Enter aceita o sugerido).",
                "Clique ao lado do poste: a CEO entra numerada (CEO-01, CEO-02...) e ligada ao poste mais próximo.",
                "Modelo troca o bloco e Numero muda a sequência no meio; Enter termina."
            },
            ["FIBRA_SPLITTER"] = new[]
            {
                "Clique na CTO ou CEO.",
                "Digite o nível (1, 2 ou 3) e a razão (2 a 64): o texto da caixa ganha \"S1 1:8\".",
                "Uma caixa pode ter um splitter de cada nível. Remover tira os splitters dela."
            },
            ["FIBRA_INSERIR_ELETRICOS"] = new[]
            {
                "Escolha o item na lista (chave, para-raio, trafo...).",
                "Clique onde ele vai; Bloco troca o item sem sair do comando.",
                "Enter termina."
            },
            ["FIBRA_INSERIR_AMARRACAO"] = new[]
            {
                "Selecione os postes (um, vários ou uma janela).",
                "Cada poste recebe uma amarração em cada direção de cabo que sai dele; as antigas são trocadas.",
                "Enter sem selecionar insere uma amarração à mão."
            },
            ["FIBRA_NUMERAR_AUTO"] = new[]
            {
                "Clique no primeiro poste (ou no texto dele). Selecionar limita a alguns postes.",
                "Digite o número dele (1, P1 ou P-01).",
                "A numeração segue os cabos; as CTO/CEO podem ser renumeradas junto."
            },
            ["FIBRA_NUMERAR_MANUAL"] = new[]
            {
                "Digite o número do primeiro poste.",
                "Clique nos postes na ordem que quiser: cada clique recebe o próximo número.",
                "Desfazer volta o último; Enter termina e avisa números repetidos."
            },
            ["FIBRA_RENUMERAR"] = new[]
            {
                "Clique no bloco (poste, CTO, CEO ou bloco com NÚMERO).",
                "No primeiro de cada tipo, digite o número novo; os seguintes recebem o próximo.",
                "Enter termina."
            },
            ["FIBRA_TROCAR_CABO"] = new[]
            {
                "Escolha o cabo novo na lista.",
                "Clique nos cabos a trocar: mudam a layer, o nome e os textos, sem desenhar de novo.",
                "Enter termina. Rode o Esforço no Percurso de novo se a tração mudou."
            },
            ["FIBRA_TROCAR_POSTE"] = new[]
            {
                "Escolha o modelo novo na lista.",
                "Clique nos postes a trocar: o número, o esforço existente e o ID Energisa ficam.",
                "Enter termina. Rode o Esforço no Percurso de novo se a altura mudou."
            },
            ["FIBRA_TAMANHO_BLOCO"] = new[]
            {
                "Selecione os blocos.",
                "Digite o tamanho do maior lado (ou Fator para multiplicar).",
                "Cada bloco muda no lugar e o texto dele acompanha."
            },
            ["FIBRA_PADRONIZAR_TAMANHOS"] = new[]
            {
                "Rode o comando (com objetos selecionados antes, só eles).",
                "Postes, CTO/CEO, textos, setas, reservas e cordoalhas vão para o tamanho da escala do desenho.",
                "Útil depois de colar parte de um desenho feito em outra escala."
            },
            ["FIBRA_ESFORCO_PERCURSO"] = new[]
            {
                "Escolha Total (todos os cabos do poste) ou Cabo (só os selecionados).",
                "Selecione os cabos do percurso.",
                "A seta entra onde a NDU 009 pede (pontas, derivações e ângulos acima de 10°); a linha de comando mostra cada poste."
            },
            ["FIBRA_ESFORCO_TOTAL"] = new[]
            {
                "Clique no poste.",
                "O esforço de todos os cabos dele é somado e comparado com o nominal; a seta entra se a norma pedir.",
                "Clique no próximo poste ou Enter para sair."
            }
        };

        /// <summary>Passo a passo numerado do comando ("1. ...", uma linha cada); null se ele não tem.</summary>
        public static string? StepsFor(string command) =>
            Steps.TryGetValue(command, out string[]? steps) ? string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}")) : null;
    }
}
