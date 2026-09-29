# Fiber Plugin - AutoCAD para Projetos de Fibra Óptica

Plugin para AutoCAD que auxilia em projetos de redes de fibra óptica (FTTH/FTTx): lançamento de cabos,
postes, CTO/CEO, cálculo de esforços, relatório em Excel e pranchas para PDF.
Compatível com **AutoCAD 2022, 2023, 2024, 2025 e 2026** (inclusive verticais como Map 3D e Civil 3D).

## Tecnologias
- Linguagem: C#
- Duas compilações do mesmo código:
  - `net48` (.NET Framework 4.8, pacote `AutoCAD.NET` 24.1) → AutoCAD 2022, 2023 e 2024
  - `net8.0-windows` (.NET 8, pacote `AutoCAD.NET` 25.1.0) → AutoCAD 2025 e 2026

## Instalação

### Instalador pronto (recomendado)
O instalador da versão atual fica em [`Instalador/FiberPlugin-1.9.13.msi`](Instalador/FiberPlugin-1.9.13.msi).
Baixe, feche o AutoCAD e execute; não precisa compilar nada. Ao abrir o AutoCAD aparece a aba **Fibra**
e a mensagem *"Fiber Plugin v&lt;versão&gt; carregado"* na linha de comando.

Para desinstalar, use Configurações > Aplicativos > "Fiber Plugin para AutoCAD".

### Verificar o computador
Clique duas vezes em [`Verificacao/Verificar.bat`](Verificacao/Verificar.bat). Ele confere Windows 64 bits,
AutoCAD 2022–2026, .NET Framework 4.8, .NET 8 Desktop Runtime e se o plugin está instalado e atualizado.
Para o que faltar, pergunta se quer instalar (pelo winget ou pelo `.msi` da pasta `Instalador`).
Opcionalmente confere também as ferramentas para compilar (.NET SDK 8 e Git).

### Primeiro uso num PC novo
1. Rode `Verificacao/Verificar.bat` (ou instale o `.msi` da pasta `Instalador`).
2. No AutoCAD, clique em **Atualizar Blocos** e escolha o `FiberPlugin/Blocos/BLOCOS.dwg` do projeto.
3. Em cada desenho, defina a **Zona UTM** e a **Escala**.

### Gerar um instalador novo
1. Altere `<Version>` em `FiberPlugin/FiberPlugin.csproj`.
2. Rode:
   ```bash
   powershell -ExecutionPolicy Bypass -File Instalacao/build.ps1
   ```
3. O `.msi` novo sai direto na pasta `Instalador`, no lugar do anterior: fica só a versão atual.
   Instalado por cima, ele substitui a versão antiga sem precisar desinstalar.

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
    └── README.md               este arquivo
```

- **Carrega sozinho** ao abrir o AutoCAD, sem `NETLOAD`.
- **Sem aviso de segurança (SECURELOAD):** `Program Files\Autodesk\ApplicationPlugins` é pasta
  confiável do AutoCAD.
- **As planilhas ficam em `Documentos\Fiber Plugin\Dados`**, editáveis sem permissão de
  administrador. Arquivos que faltarem são copiados do pacote na abertura do AutoCAD, sem sobrescrever
  os existentes. Atualizar ou desinstalar o plugin não apaga nada dessa pasta.
- **Os blocos vêm de um único `BLOCOS.dwg`**: o escolhido no botão **Atualizar Blocos** (ex.: o da pasta
  do projeto) ou, sem ele, a cópia que vem no pacote. Com o arquivo escolhido, um bloco novo aparece só
  de salvar o `BLOCOS.dwg`, sem reinstalar.

### Assinatura digital (opcional)
Só é necessária para carregar a DLL de fora das pastas confiáveis ou para distribuir a terceiros.
Com um certificado de assinatura de código instalado no Windows:
```bash
powershell -ExecutionPolicy Bypass -File Instalacao/build.ps1 -Certificado <thumbprint>
```
Para testes internos, `Instalacao/criar-certificado-teste.ps1` gera um certificado autoassinado.

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
AutoCad_BD/
├── FiberPlugin/            código do plugin
│   ├── Commands/           comandos do AutoCAD
│   ├── Core/               rotinas compartilhadas (cálculos, desenho, biblioteca de blocos)
│   ├── Models/             catálogo de cabos
│   ├── UI/                 aba Fibra, menu e janelas
│   ├── Dados/              planilhas editáveis no Excel
│   │   ├── cabos.csv       cabos cadastrados (NomeCompleto;NomeCurto;Peso_kg_km)
│   │   └── postes.csv      modelos de poste (Tipo;Altura_m;Esforco_daN)
│   └── Blocos/
│       └── BLOCOS.dwg      todos os blocos do plugin (a única biblioteca)
├── Instalacao/             como gerar o instalador
│   ├── build.ps1           compila e gera o .msi em Instalador/
│   ├── PackageContents.xml manifesto do Autoloader do AutoCAD
│   ├── Installer/          projeto WiX do .msi (e texto da tela de instalação)
│   └── criar-certificado-teste.ps1   opcional, para assinar as DLLs
├── Instalador/
│   └── FiberPlugin-<versão>.msi      instalador da versão atual (só um)
└── Verificacao/
    ├── Verificar.bat       clique duas vezes para conferir o computador
    └── verificar.ps1
```

**Dados**: as planilhas são lidas a cada comando. Basta editar no Excel e salvar, sem recompilar.
Aceita CSV salvo pelo Excel em português (separador `;`, vírgula decimal).

**Blocos**: o plugin usa **somente** os blocos do `BLOCOS.dwg`. Para acrescentar, alterar ou remover um
bloco, edite esse arquivo, salve e clique em **Atualizar Blocos**. Ao inserir, a definição é copiada do
`BLOCOS.dwg` automaticamente, sem template; se o desenho já tiver um bloco com o mesmo nome, ele é
substituído pela versão do `BLOCOS.dwg`. O grupo de cada bloco vem do nome e define o comando que o insere:
POSTE/DT/CC → Postes, CTO → CTO, CEO → CEO, TRAFO/CHAVE/PARA-RAIO/ATERRAMENTO → Elétricos,
AMARRAÇÃO → Amarração, o resto → Outros Blocos.
Um `BLOCOS.bak` que apareça na pasta é o backup automático do AutoCAD ao salvar: pode apagar.

Regras dos blocos:
- **Postes DT e CC**: o Inserir Postes usa um bloco para cada tipo, achado pelo nome nesta ordem:
  exatamente `DT`/`CC`; nome com DT ou CC como palavra (ex.: `POSTE DT`); `DUPLO T`/`CIRCULAR`.
- **Número**: atributo `NUMERO` (ou `NÚMERO`/`ID`), preenchido com P-01, CTO-01... e usado pelo Renumerar.
- **Tipo do poste**: atributo `NOME` (ou `TIPO`/`INFO`/`DESCRICAO`), preenchido com a altura/esforço (11/300).
- **Coordenadas**: atributos `COORDENADA_X`, `COORDENADA_Y` e `ZONA` são preenchidos na inserção
  (postes, elétricos e outros blocos).
- **Seta de esforço**: se o `BLOCOS.dwg` tiver o bloco `SETA DE ESFORÇO`, ele é usado, com os atributos
  `ESFORCO_KFG` (valor) e `ANGULO`; sem ele, o plugin desenha a seta.

## Comandos

Todos ficam na aba **Fibra** da faixa de opções e no menu `FIBRA`.

### Cabos
| Botão | Comando | Função |
|---|---|---|
| Lançar Cabo | `FIBRA_LANCAR_CABO` | Desenha o cabo clicando nos pontos, como o comando LINE: cada trecho aparece na tela ao clicar, **D** desfaz o último ponto e Enter finaliza. Escreve nome e metragem vão a vão. |
| Roteamento Automático | `FIBRA_ROTEAMENTO_AUTO` | Seleciona blocos e gera a rota mais curta entre eles (vizinho mais próximo testando todos os inícios + otimização 2-opt), afastada 1,8 m dos postes. |

### Inserir
| Botão | Comando | Função |
|---|---|---|
| Postes | `FIBRA_INSERIR_POSTE` | Escolhe o modelo na lista (DT ou CC 11/300, 11/600, 11/1000, 12/1000, 11/1500, 12/1500 e DT 9/200, de `Dados/postes.csv`) e insere o bloco DT ou CC já numerado (P-01, P-02...), com texto de número, altura/esforço e coordenada UTM. O poste **DT** gira acompanhando o mouse depois do clique (clique para fixar, digite o ângulo ou Enter = 0°); o CC não gira. **M** troca o modelo e **N** o número durante a inserção. |
| CTO | `FIBRA_INSERIR_CTO` | Insere CTO numerada (CTO-01, CTO-02...), vinculada ao poste mais próximo, sem coordenadas. O símbolo sai com **7 mm no papel** no maior lado (7 m em 1:1000; acompanha a Escala), centrado no clique, e o nome fica centralizado logo abaixo. |
| CEO | `FIBRA_INSERIR_CEO` | Igual à CTO, para CEO (blocos `CEO_1`, `CEO_2`: **M** troca o modelo). |
| Elétricos | `FIBRA_INSERIR_ELETRICOS` | TRAFO, TRAFO COM CHAVE FU, CHAVE FU, CHAVE CH, PARA-RAIO, ATERRAMENTO. |
| Amarração | `FIBRA_INSERIR_AMARRACAO` | Depois do ponto, o bloco gira acompanhando o mouse até o clique (ou digite o ângulo). Sem coordenadas. |
| Outros Blocos | `FIBRA_INSERIR_BLOCO` | Blocos do `BLOCOS.dwg` que não pertencem a nenhum grupo. |
| Renumerar | `FIBRA_RENUMERAR` | Corrige a numeração: clique nos blocos na ordem desejada. No primeiro de cada tipo você digita o número; os seguintes recebem o próximo automaticamente. Funciona em postes, CTO, CEO e qualquer bloco com atributo NÚMERO/ID; atualiza atributo, texto de identificação (sem mudar de lugar) e dados do relatório. **N** define o próximo número; avisa se o número já existir em outro bloco. |
| Tamanho | `FIBRA_TAMANHO_BLOCO` | Muda o tamanho de blocos já inseridos: selecione um ou vários e digite o novo tamanho (maior lado, em m) ou use **Fator** (2 = dobro, 0,5 = metade). Cada bloco continua no ponto em que foi inserido (CTO/CEO: no centro do símbolo) e o texto de identificação acompanha. Também funciona selecionando antes. |

### Esforços
| Botão | Comando | Função |
|---|---|---|
| Esforço no Poste | `FIBRA_ESFORCO_TOTAL` | Esforço resultante de todos os cabos no poste clicado, comparado com o nominal. |
| Esforço no Percurso | `FIBRA_ESFORCO_PERCURSO` | Seta de esforço em todos os postes do percurso dos cabos selecionados (total no poste ou só o cabo). Cada ponto é vinculado ao poste mais próximo (até 10 m) e guarda a situação (fim de rede, passagem ou ângulo). |

### Relatório e Pranchas
| Botão | Comando | Função |
|---|---|---|
| Gerar Relatório | `FIBRA_RELATORIO` | Excel (.xlsx) com as abas **Resumo**, **Postes** (tipo, altura, esforço nominal, coordenadas e esforço calculado), **CTO e CEO** (ID, bloco, poste vinculado e coordenadas), **Esforços** (cada ponto ligado ao poste de onde sai o cálculo) e **Cabos** (metragem e lances por tipo). |
| Gerar Folhas | `FIBRA_GERAR_FOLHAS` | Divide uma área do projeto em folhas A0 a A4 na escala escolhida, com moldura e viewport, prontas para PDF (veja abaixo). |

### Fiber Plugin
| Botão | Comando | Função |
|---|---|---|
| Zona UTM | `FIBRA_ZONA_UTM` | Zona UTM e hemisfério do projeto (gravados no DWG). As coordenadas saem no formato `20 L` / `405110.92 m E` / `9032585.41 m S`, com a letra da faixa de latitude calculada por ponto. Oferece atualizar os textos dos postes já inseridos. |
| Escala | `FIBRA_ESCALA` | Escala do desenho (1:500, 1:1000, 1:2000...): textos, setas de esforço e CTO/CEO novas saem no tamanho certo, e as anotações existentes podem ser ajustadas. |
| Pasta de Dados | `FIBRA_ABRIR_PASTA` | Abre a pasta com as planilhas de cabos e postes. |
| Atualizar Blocos | `FIBRA_ATUALIZAR_BLOCOS` | Na primeira vez pergunta qual `BLOCOS.dwg` usar; daí em diante o plugin lê direto dele. Também troca no desenho aberto os blocos que mudaram e lista os blocos novos. **Trocar** escolhe outro arquivo; **Padrao** volta ao `BLOCOS.dwg` do plugin. |

### Só pela linha de comando
| Comando | Função |
|---|---|
| `FIBRA` | Menu com todas as ferramentas, com busca. |
| `FIBRA_CALCULAR_ESFORCO` | Esforço de um único cabo numa sequência de postes clicados. |
| `FIBRA_EXPORTAR_BLOCOS` | Copia os blocos do desenho aberto para dentro do `BLOCOS.dwg` escolhido no Atualizar Blocos. |
| `FIBRA_RIBBON` | Recria a aba "Fibra" (se ela sumir após trocar de espaço de trabalho). |
| `FIBRA_SOBRE` | Mostra a versão instalada, a escala e as pastas e o `BLOCOS.dwg` em uso. |

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

Os textos dos vãos, os textos de identificação, a seta de esforço e o símbolo da CTO/CEO têm tamanho
definido para **1:1000** (texto de 2 mm e CTO/CEO de 7 mm no papel). Com `FIBRA_ESCALA` você informa a
escala do projeto e tudo passa a ser criado proporcionalmente: em 1:2000 fica o dobro, em 1:500 a metade.
A escala fica gravada no próprio DWG, e desenhos sem escala definida usam 1:1000.

Ao trocar a escala, o comando oferece ajustar as anotações já desenhadas: altura dos textos,
afastamento dos textos em relação ao cabo e tamanho das setas, sempre em torno do poste.
Os blocos já inseridos não mudam de tamanho; para isso use o botão **Tamanho**.

## Premissas de cálculo

**Esforço nos postes**
- Tração de cada vão pela aproximação da parábola, com flecha de 1% do vão:
  `T = p·L² / (8·f) = 12,5 · p · L` (p em kgf/m), em **kgf** (a unidade usada em todo o plugin).
- O esforço no poste é a soma vetorial das trações dos vãos que chegam nele. De cada cabo é
  considerado o vértice mais próximo do poste, num raio de 2,5 m.
- **Não considera** vento, variação de temperatura, desnível entre postes nem a tração máxima de
  projeto do fabricante do cabo. Para um laudo formal, confirme com a norma da concessionária.
- O esforço nominal vem do modelo escolhido no `FIBRA_INSERIR_POSTE` (ou, em desenhos antigos, do nome
  do poste). Ele segue a norma em daN e é convertido para kgf: `DT 11/200` → 200 daN = 204 kgf.

## Identificação dos elementos
Cabos, postes, CTO/CEO e setas de esforço guardam seus dados em XData (aplicação `FIBRA_PLUGIN`), então
renomear layers não quebra os cálculos nem o relatório. Cabos desenhados por versões antigas do plugin
continuam sendo reconhecidos pelo nome da layer (`FIBRA_CABO_...`).
