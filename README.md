# Plugin para AutoCAD - Projetos de Fibra Óptica

Projeto base para o desenvolvimento de um plugin para AutoCAD 2026 com o objetivo de auxiliar em projetos de redes de fibra óptica (FTTH/FTTx).

## Tecnologias Previstas
- Linguagem: C# (.NET)
- API: AutoCAD .NET API (Gerenciamento de blocos, linhas, layers e atributos)

## Comandos Disponíveis

1. **`FIBRA_INSERIR_BLOCO`**: Comando para inserir postes, caixas de emenda (CEO), e caixas de terminação (CTO) no projeto através de uma interface de seleção intuitiva.
2. **`FIBRA_LANCAR_CABO`**: Comando para desenhar cabos de fibra interligando os elementos. Permite o clique contínuo gerando uma polilinha e calculando a metragem total do cabo.
3. **`FIBRA_EXPORTAR_CSV`**: Comando para varrer o projeto atual, contabilizar a quantidade de todos os blocos inseridos, somar a metragem total de cabos desenhados e exportar a Lista de Materiais (BOM) em um arquivo padrão `.csv`.
4. **`FIBRA_NUMERAR_PONTOS`**: Comando para clicar nos postes/pontos do desenho e automaticamente gerar as coordenadas UTM (X e Y) no modelo, além de numerar sequencialmente (P01, P02...).
5. **`FIBRA_CALCULAR_BOBINAS`**: Comando para contabilizar a metragem total de todos os cabos no projeto, permitindo especificar o tamanho da bobina padrão e uma margem de segurança para calcular a quantidade exata de bobinas necessárias.
6. **`FIBRA_ROTEAMENTO_AUTO`**: Comando que permite selecionar múltiplos blocos (elementos da rede) e automaticamente roteia e desenha os cabos interligando o caminho mais curto entre eles (Nearest Neighbor).

## Possíveis Funcionalidades (A definir)
1. Inserção automatizada de elementos (Postes, CTOs, CEOs).
2. Cálculo de atenuação de rede (budget óptico).
3. Geração de relatórios e documentação em PDF.
