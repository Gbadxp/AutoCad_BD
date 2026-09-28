# Plugin para AutoCAD - Projetos de Fibra Óptica

Plugin para AutoCAD que auxilia em projetos de redes de fibra óptica (FTTH/FTTx).
Compatível com **AutoCAD 2022, 2023, 2024, 2025 e 2026** (inclusive verticais como Map 3D e Civil 3D).

## Tecnologias
- Linguagem: C#
- Duas compilações do mesmo código:
  - `net48` (.NET Framework 4.8, pacote `AutoCAD.NET` 24.1) → AutoCAD 2022, 2023 e 2024
  - `net8.0-windows` (.NET 8, pacote `AutoCAD.NET` 25.1.0) → AutoCAD 2025 e 2026

## Instalação

Para conferir se o computador tem tudo o que o plugin precisa (AutoCAD, .NET, plugin atualizado), clique duas vezes em [`Verificacao/Verificar.bat`](Verificacao/Verificar.bat): ele mostra o que falta e pergunta se quer instalar.

### Opção 1: instalador (recomendado)
O instalador pronto da versão atual fica em [`Instalador/FiberPlugin-1.9.6.msi`](Instalador/FiberPlugin-1.9.6.msi): baixe, feche o AutoCAD e execute (não precisa compilar). Para gerar um novo:

1. Gere o pacote (uma vez por versão):
   ```bash
   powershell -ExecutionPolicy Bypass -File Instalacao/build.ps1
   ```
2. Feche o AutoCAD e execute `Instalacao/dist/FiberPlugin-<versão>.msi`.
3. Abra o AutoCAD: aparece a aba **Fibra** na faixa de opções e a mensagem
   *"Fiber Plugin v1.0.0 carregado"* na linha de comando.

Para desinstalar, use Configurações > Aplicativos > "Fiber Plugin para AutoCAD".

### Opção 2: script
Depois do `build.ps1`, rode `Instalacao/instalar.ps1`, que copia o pacote e pede permissão de
administrador. Para remover, use `Instalacao/desinstalar.ps1`.

### Como funciona
O plugin é instalado como pacote do **Autoloader** do AutoCAD em
`C:\Program Files\Autodesk\ApplicationPlugins\FiberPlugin.bundle`:

```
FiberPlugin.bundle/
├── PackageContents.xml     diz ao AutoCAD qual DLL carregar em cada versão
└── Contents/
    ├── net48/FiberPlugin.dll   AutoCAD 2022-2024
    ├── net8/FiberPlugin.dll    AutoCAD 2025-2026
    ├── Dados/  Blocos/         conteúdo padrão
    └── LEIA-ME.txt
```

- **Carrega sozinho** ao abrir o AutoCAD, sem `NETLOAD`.
- **Sem aviso de segurança (SECURELOAD):** `Program Files\Autodesk\ApplicationPlugins` é pasta
  confiável do AutoCAD.
- **As planilhas ficam em `Documentos\Fiber Plugin\Dados`**, editáveis sem permissão de
  administrador. Arquivos que faltarem são copiados do pacote na abertura do AutoCAD, sem sobrescrever
  os existentes. Atualizar ou desinstalar o plugin não apaga nada dessa pasta.
- **Os blocos vêm no `BLOCOS.dwg` do pacote** e são atualizados a cada versão instalada: para mudar os
  blocos da equipe, edite `FiberPlugin/Blocos/BLOCOS.dwg`, rode o `build.ps1` e instale o `.msi` novo.
  `Documentos\Fiber Plugin\Blocos` é opcional, para blocos pessoais (têm prioridade em nomes repetidos).

### Assinatura digital (opcional)
Só é necessária para carregar a DLL de fora das pastas confiáveis ou para distribuir a terceiros.
Com um certificado de assinatura de código instalado no Windows:
```bash
powershell -ExecutionPolicy Bypass -File Instalacao/build.ps1 -Certificado <thumbprint>
```
Para testes internos, `Instalacao/criar-certificado-teste.ps1` gera um certificado autoassinado.

### Nova versão
Altere `<Version>` em `FiberPlugin/FiberPlugin.csproj` e rode o `build.ps1` de novo. O `.msi` novo
substitui a versão anterior automaticamente.

## Desenvolvimento

```bash
cd FiberPlugin
dotnet build
```

No AutoCAD, rode `NETLOAD` e escolha a DLL da versão certa:
- AutoCAD 2022-2024: `FiberPlugin\bin\Debug\net48\FiberPlugin.dll`
- AutoCAD 2025-2026: `FiberPlugin\bin\Debug\net8.0-windows\FiberPlugin.dll`

Carregado por `NETLOAD`, o plugin usa diretamente as pastas `FiberPlugin/Dados` e `FiberPlugin/Blocos`
do código-fonte. Se a versão instalada também estiver ativa, desinstale-a antes de testar para não
carregar duas cópias.

## Estrutura de pastas

```
FiberPlugin/
├── Commands/   comandos do AutoCAD (um arquivo por comando)
├── Core/       rotinas compartilhadas (cálculos, desenho, biblioteca de blocos)
├── Models/     catálogo de cabos
├── UI/         janelas
├── Dados/      planilhas editáveis no Excel
│   ├── cabos.csv            cabos cadastrados (NomeCompleto;NomeCurto;Peso_kg_km)
│   └── postes.csv           modelos de poste do Inserir Postes (Tipo;Altura_m;Esforco_daN)
└── Blocos/     biblioteca de blocos
    ├── BLOCOS.dwg           todos os blocos do plugin (definições de bloco)
    └── LEIA-ME.txt
```

**Dados**: as planilhas são lidas a cada comando. Basta editar no Excel e salvar, sem recompilar.
Aceita CSV salvo pelo Excel em português (separador `;`, vírgula decimal).

**Blocos**: o plugin usa **somente** os blocos definidos no `BLOCOS.dwg`. Para acrescentar, alterar
ou remover um bloco, edite esse arquivo e salve. Ao inserir, a definição é copiada do `BLOCOS.dwg`
automaticamente, sem template; se o desenho já tiver um bloco com o mesmo nome (ex.: de um template antigo),
ele é substituído pela versão do `BLOCOS.dwg`. O
grupo de cada bloco vem do nome e define o comando que o insere: POSTE → Inserir Postes, CTO → Inserir CTO,
CEO → Inserir CEO, TRAFO/CHAVE/PARA-RAIO/ATERRAMENTO → Elétricos, AMARRAÇÃO → Amarração, o resto → Outros Blocos.
`FIBRA_EXPORTAR_BLOCOS` copia os blocos do desenho aberto para dentro do `BLOCOS.dwg`.

Instalado, o plugin usa `Documentos\Fiber Plugin\Dados` e o `BLOCOS.dwg` do pacote (mais os blocos
pessoais de `Documentos\Fiber Plugin\Blocos`, se houver). Em desenvolvimento, usa as pastas do código-fonte.

## Comandos Disponíveis

| Comando | Função |
|---|---|
| `FIBRA` | Menu com todas as ferramentas (digite o comando; não há botão na aba). |
| `FIBRA_LANCAR_CABO` | Desenha o cabo clicando nos pontos, como o comando LINE: cada trecho aparece na tela ao clicar, **D** desfaz o último ponto e Enter finaliza. Escreve nome e metragem vão a vão. |
| `FIBRA_ROTEAMENTO_AUTO` | Seleciona blocos e gera a rota mais curta entre eles (vizinho mais próximo testando todos os inícios + otimização 2-opt), afastada 1,8 m dos postes. |
| `FIBRA_INSERIR_POSTE` | Insere postes escolhendo o modelo na lista (DT ou CC 11/300, 11/600, 11/1000, 12/1000, 11/1500, 12/1500 e DT 9/200), com o bloco DT ou CC do `BLOCOS.dwg`. Cada poste já sai numerado e com o texto de número, altura/esforço e coordenada UTM (zona, E e N). A zona UTM vem do botão Zona UTM. Os modelos ficam em `Dados/postes.csv`. O poste **DT** gira acompanhando o mouse depois do clique (clique para fixar, digite o ângulo ou Enter = 0°); o CC, circular, não gira. |
| `FIBRA_INSERIR_CTO` | Insere CTO (bloco `CTO`) numerada em sequência (CTO-01, CTO-02...), com texto de identificação (sem coordenadas), vinculada ao poste mais próximo. O símbolo é ajustado para 6 m no maior lado (na escala 1:1000) e centrado no clique, seja qual for a unidade em que o bloco foi desenhado. |
| `FIBRA_INSERIR_CEO` | Igual ao da CTO, para CEO (blocos `CEO_1`, `CEO_2`: escolha o modelo; **M** troca durante a inserção). |
| `FIBRA_INSERIR_ELETRICOS` | Itens elétricos: TRAFO, TRAFO COM CHAVE FU, CHAVE FU, CHAVE CH, PARA-RAIO, ATERRAMENTO. |
| `FIBRA_INSERIR_AMARRACAO` | Amarração: depois do ponto, o bloco gira acompanhando o mouse até o clique (ou digite o ângulo). Sem coordenadas. |
| `FIBRA_INSERIR_BLOCO` | Outros blocos do `BLOCOS.dwg` que não pertencem a nenhum grupo. |
| `FIBRA_RENUMERAR` | Corrige a numeração: clique nos blocos na ordem desejada. No primeiro de cada tipo você digita o número; os seguintes recebem o próximo automaticamente (P-05, P-06...). Funciona em postes, CTO, CEO e qualquer bloco com atributo NÚMERO/ID; atualiza atributo, texto ao lado (sem mudar de lugar) e dados do relatório. **N** define o próximo número; avisa se o número já existir em outro bloco. |
| `FIBRA_CALCULAR_ESFORCO` | Esforço de um único cabo numa sequência de postes clicados (fora do menu). |
| `FIBRA_ESFORCO_TOTAL` | Esforço resultante de todos os cabos num poste, comparado com o nominal. |
| `FIBRA_ESFORCO_PERCURSO` | Coloca a seta de esforço em todos os postes do percurso dos cabos selecionados (total no poste ou só o cabo). Cada ponto é vinculado ao poste mais próximo (até 10 m) e guarda a situação (fim de rede, passagem ou ângulo). |
| `FIBRA_RELATORIO` | Relatório único em Excel (.xlsx) com as abas **Resumo**, **Postes** (tipo, altura, esforço nominal, coordenadas e esforço calculado), **CTO e CEO** (ID, bloco, poste vinculado e coordenadas), **Esforços** (cada ponto ligado ao poste de onde sai o cálculo: situação, kgf, ângulo e resultado) e **Cabos** (metragem e lances por tipo). |
| `FIBRA_GERAR_FOLHAS` | Divide uma área do projeto em folhas (A0 a A4) na escala escolhida, criando um layout com moldura e viewport para cada pedaço, pronto para PDF. |
| `FIBRA_ZONA_UTM` | Zona UTM e hemisfério do projeto (gravados no DWG). As coordenadas saem no formato `20 L` / `405110.92 m E` / `9032585.41 m S`, com a letra da faixa de latitude calculada para cada ponto. Oferece atualizar os textos dos postes. |
| `FIBRA_ESCALA` | Define a escala do desenho (1:500, 1:1000, 1:2000...). Textos e setas de esforço passam a sair no tamanho certo, e as anotações existentes podem ser ajustadas. |
| `FIBRA_EXPORTAR_BLOCOS` | Copia os blocos do desenho aberto para dentro do `BLOCOS.dwg`. Sem botão: digite o comando. |
| `FIBRA_ABRIR_PASTA` | Abre a pasta com a planilha de cabos e a biblioteca de blocos. |
| `FIBRA_RIBBON` | Recria a aba "Fibra" (se ela sumir após trocar de espaço de trabalho). |
| `FIBRA_SOBRE` | Mostra a versão instalada e as pastas em uso. |

## Folhas para impressão (PDF)

`FIBRA_GERAR_FOLHAS` monta o jogo de pranchas a partir do Model:

1. Marque a área com dois cliques (retângulo em volta do trecho do projeto).
2. Escolha a folha (A0 a A4), a escala (padrão: a do `FIBRA_ESCALA`), a sobreposição entre folhas
   vizinhas (padrão 5%) e o prefixo dos nomes (padrão `FL`).
3. O plugin divide a área numa grade de folhas, escolhe a orientação (paisagem ou retrato) que usa
   menos folhas e descarta os pedaços sem nenhum elemento do desenho.
4. Cada folha vira um layout (`FL-01`, `FL-02`...) com:
   - configuração de página **DWG To PDF**, papel do formato escolhido, plotagem 1:1;
   - moldura (margem de 25 mm à esquerda e 10 mm nas demais) e faixa com número da folha, escala e formato;
   - viewport travado na escala exata, mostrando o pedaço correspondente.
5. No Model aparecem os retângulos e nomes das folhas (layer `FIBRA_FOLHAS`, que não imprime).

Para gerar o PDF, use o comando **PUBLICAR** (PUBLISH) do AutoCAD e selecione as folhas.
Rodar de novo com o mesmo prefixo substitui as folhas anteriores. O desenho deve estar em metros.

## Escala das anotações

Os textos dos vãos, os textos de identificação dos postes e a seta de esforço têm tamanho definido para
**1:1000** (texto de 2 mm no papel). Com `FIBRA_ESCALA` você informa a escala do projeto e tudo
passa a ser criado proporcionalmente: em 1:2000 fica o dobro, em 1:500 a metade. A escala fica
gravada no próprio DWG, e desenhos sem escala definida usam 1:1000.

Ao trocar a escala, o comando oferece ajustar as anotações já desenhadas: altura dos textos,
afastamento dos textos em relação ao cabo e tamanho das setas, sempre em torno do poste.
Os blocos (postes, CTO, CEO) não mudam de tamanho.

## Premissas de cálculo

**Esforço nos postes**
- Tração de cada vão pela aproximação da parábola, com flecha de 1% do vão:
  `T = p·L² / (8·f) = 12,5 · p · L` (p em kgf/m), em **kgf** (a unidade usada em todo o plugin).
- O esforço no poste é a soma vetorial das trações dos vãos que chegam nele. De cada cabo é
  considerado o vértice mais próximo do poste, num raio de 2,5 m.
- **Não considera** vento, variação de temperatura, desnível entre postes nem a tração máxima de
  projeto do fabricante do cabo. Para um laudo formal, confirme com a norma da concessionária.
- O esforço nominal vem do modelo escolhido no `FIBRA_INSERIR_POSTE` (ou, em desenhos antigos, do nome do poste). Ele segue a norma em daN e é convertido para kgf:
  `DT 11/200` → 200 daN = 204 kgf.

## Identificação dos cabos
Cada cabo desenhado guarda o tipo em XData (aplicação `FIBRA_PLUGIN`), então renomear a layer não
quebra os cálculos. Cabos desenhados por versões antigas do plugin continuam sendo reconhecidos
pelo nome da layer (`FIBRA_CABO_...`).
