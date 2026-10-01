# Fiber Plugin - AutoCAD para Projetos de Fibra Óptica

Plugin para AutoCAD que auxilia em projetos de redes de fibra óptica (FTTH/FTTx): lançamento de cabos,
postes, CTO/CEO, cálculo de esforços, relatório em Excel e pranchas para PDF.
Compatível com **AutoCAD 2022, 2023, 2024, 2025 e 2026** (inclusive verticais como Map 3D e Civil 3D).

## Tecnologias
- Linguagem: C#
- Duas compilações do mesmo código:
  - `net48` (.NET Framework 4.8, pacote `AutoCAD.NET` 24.1) → AutoCAD 2022, 2023 e 2024
  - `net8.0-windows` (.NET 8, pacote `AutoCAD.NET` 25.1.0) → AutoCAD 2025 e 2026
- [Clipper2](https://github.com/AngusJohnson/Clipper2) (licença Boost) para unir e recortar o contorno das ruas

## Instalação

### Instalador pronto (recomendado)
O instalador da versão atual fica em [`Instalador/FiberPlugin-1.9.24.msi`](Instalador/FiberPlugin-1.9.24.msi).
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
3. Em cada desenho, defina a **Zona UTM**, a **Escala** e os **Parâmetros** de cálculo.

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
    ├── net48/FiberPlugin.dll   AutoCAD 2022-2024 (+ Clipper2Lib.dll)
    ├── net8/FiberPlugin.dll    AutoCAD 2025-2026 (+ Clipper2Lib.dll)
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
│   ├── Dados/              dados editáveis (Excel / Bloco de Notas)
│   │   ├── cabos.csv       cabos (NomeCompleto;NomeCurto;Peso_kg_km;Fibras;Diametro_mm)
│   │   ├── postes.csv      modelos de poste (Tipo;Altura_m;Esforco_daN)
│   │   ├── tracao_ndu009.csv  Tabela 08 da NDU 009 (tração por fibras e vão)
│   │   ├── empresa.txt     dados da empresa para o Memorial Descritivo
│   │   └── Memorial/       logo.png e figuras de instalação do memorial
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
AMARRAÇÃO → Amarração. Bloco com outro nome não entra em nenhum comando de inserir.
Um `BLOCOS.bak` que apareça na pasta é o backup automático do AutoCAD ao salvar: pode apagar.

Regras dos blocos:
- **Postes DT e CC**: o Inserir Postes usa um bloco para cada tipo, achado pelo nome nesta ordem:
  exatamente `DT`/`CC`; nome com DT ou CC como palavra (ex.: `POSTE DT`); `DUPLO T`/`CIRCULAR`.
- **Número**: atributo `NUMERO` (ou `NÚMERO`/`ID`), preenchido com P-01, CTO-01... e usado pelo Renumerar.
- **Tipo do poste**: atributo `NOME` (ou `TIPO`/`INFO`/`DESCRICAO`), preenchido com a altura/esforço (11/300).
- **Coordenadas**: atributos `COORDENADA_X`, `COORDENADA_Y` e `ZONA` são preenchidos na inserção
  (postes e elétricos).
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
| Amarração | `FIBRA_INSERIR_AMARRACAO` | Selecione os postes (um, vários ou uma janela; também dá para selecionar antes): cada poste recebe uma amarração em cada direção de cabo que sai dele, sobre a linha do cabo, com a parte redonda logo depois da borda do poste e a parte aberta apontando para o cabo. Passagem = uma de cada lado, derivação com 3 linhas = uma em cada linha, fim de rede = só uma. Rodar de novo troca as amarrações que já estavam em volta do poste. Os cabos são os vértices a até 2,5 m do poste (como no cálculo de esforço). **Enter** sem selecionar insere uma à mão: ponto e direção com o mouse. Sem coordenadas. |
| Renumerar | `FIBRA_RENUMERAR` | Corrige a numeração: clique nos blocos na ordem desejada. No primeiro de cada tipo você digita o número; os seguintes recebem o próximo automaticamente. Funciona em postes, CTO, CEO e qualquer bloco com atributo NÚMERO/ID; atualiza atributo, texto de identificação (sem mudar de lugar) e dados do relatório. **N** define o próximo número; avisa se o número já existir em outro bloco. |
| Tamanho | `FIBRA_TAMANHO_BLOCO` | Muda o tamanho de blocos já inseridos: selecione um ou vários e digite o novo tamanho (maior lado, em m) ou use **Fator** (2 = dobro, 0,5 = metade). Cada bloco continua no ponto em que foi inserido (CTO/CEO: no centro do símbolo) e o texto de identificação acompanha. Também funciona selecionando antes. |

### Esforços
| Botão | Comando | Função |
|---|---|---|
| Esforço no Poste | `FIBRA_ESFORCO_TOTAL` | Esforço resultante de todos os cabos no poste clicado, comparado com o nominal. |
| Esforço no Percurso | `FIBRA_ESFORCO_PERCURSO` | Seta de esforço em todos os postes do percurso dos cabos selecionados (total no poste ou só o cabo). Cada ponto é vinculado ao poste mais próximo (até 10 m) e guarda a situação (fim de rede, passagem ou ângulo). |
| Parâmetros | `FIBRA_PARAMETROS` | Altura de fixação do cabo no poste (NDU 009: 5,20 a 5,70 m; padrão 5,40), tração pela **Tabela** 08 da norma ou pelo **Peso** do cabo, e altura mínima do cabo ao solo (Tabela 02: ruas 5,0 m). Gravados no DWG. |
| Esforço Existente | `FIBRA_ESFORCO_EXISTENTE` | Esforço que já existe nos postes selecionados (redes da Energisa e outras ocupantes), em kgf a 20 cm do topo; é somado ao do projeto na comparação com o nominal. |

### Documentos e Pranchas
| Botão | Comando | Função |
|---|---|---|
| Verificar Projeto | `FIBRA_VERIFICAR` | Confere o desenho com a NDU 009 e marca cada não conformidade com um círculo vermelho numa layer que não imprime (`FIBRA_VERIFICACAO`). Veja abaixo. |
| Gerar Relatório | `FIBRA_RELATORIO` | Excel (.xlsx) com as abas **Resumo**, **Postes** (tipo, altura, nominal, coordenadas, esforço no cabo, a 20 cm do topo, existente e total), **CTO e CEO**, **Esforços**, **Cabos**, **Tabela A (NDU 009)** (obrigatória no projeto) e **Verificação NDU 009**. |
| Memorial Descritivo | `FIBRA_MEMORIAL` | PDF do memorial para a concessionária (veja abaixo). |
| Gerar Folhas | `FIBRA_GERAR_FOLHAS` | Divide uma área do projeto em folhas A0 a A4 na escala escolhida, com moldura e viewport, prontas para PDF (veja abaixo). |

### Google Earth
| Botão | Comando | Função |
|---|---|---|
| Importar KML | `FIBRA_IMPORTAR_KML` | Traz pontos, linhas e polígonos de um KML/KMZ para o desenho, na zona UTM do projeto (veja abaixo). |
| Exportar KML | `FIBRA_EXPORTAR_KML` | Gera um KMZ (ou KML) do projeto para abrir no Google Earth (veja abaixo). |
| Importar Ruas | `FIBRA_IMPORTAR_RUAS` | Baixa as ruas do OpenStreetMap de uma área e desenha na posição UTM do projeto (veja abaixo). |

### Fiber Plugin
| Botão | Comando | Função |
|---|---|---|
| Zona UTM | `FIBRA_ZONA_UTM` | Zona UTM e hemisfério do projeto (gravados no DWG). As coordenadas saem no formato `20 L` / `405110.92 m E` / `9032585.41 m S`, com a letra da faixa de latitude calculada por ponto. Oferece atualizar os textos dos postes já inseridos. |
| Escala | `FIBRA_ESCALA` | Escala do desenho (1:500, 1:1000, 1:2000...): textos, setas de esforço e CTO/CEO novas saem no tamanho certo, e as anotações existentes podem ser ajustadas. |
| Pasta de Dados | `FIBRA_ABRIR_PASTA` | Abre a pasta com as planilhas de cabos e postes e os dados do memorial (empresa.txt, logo e figuras). |
| Atualizar Blocos | `FIBRA_ATUALIZAR_BLOCOS` | Na primeira vez pergunta qual `BLOCOS.dwg` usar; daí em diante o plugin lê direto dele. Também troca no desenho aberto os blocos que mudaram e lista os blocos novos. **Trocar** escolhe outro arquivo; **Padrao** volta ao `BLOCOS.dwg` do plugin. |

### Só pela linha de comando
| Comando | Função |
|---|---|
| `FIBRA` | Menu com todas as ferramentas, com busca. |
| `FIBRA_CALCULAR_ESFORCO` | Esforço de um único cabo numa sequência de postes clicados. |
| `FIBRA_EXPORTAR_BLOCOS` | Copia os blocos do desenho aberto para dentro do `BLOCOS.dwg` escolhido no Atualizar Blocos. |
| `FIBRA_RIBBON` | Recria a aba "Fibra" (se ela sumir após trocar de espaço de trabalho). |
| `FIBRA_SOBRE` | Mostra a versão instalada, a escala e as pastas e o `BLOCOS.dwg` em uso. |
| `FIBRA_ID_ENERGISA` | ID do poste fornecido pela Energisa (ID_Poste da Tabela A): clique em cada poste e digite o ID. Ele passa a aparecer no texto do poste e na Tabela A. Sem botão por enquanto. |

## Memorial Descritivo (PDF)

O botão **Memorial Descritivo** gera o PDF que acompanha o pedido de ocupação de postes:

1. **Capa** com logo, percurso, solicitante, concessionária e contrato, responsável técnico e ART e os totais.
2. **Ofício** à concessionária, com a qualificação da empresa e do representante, o contrato e a assinatura.
3. **Memorial**: dados da empresa, objetivo, percurso, cabos ópticos, postes por tipo, cabos por tipo
   (lances e metragem), postes para aluguel e resumo de pontos de fixação.
4. **Cálculo de esforços**: parâmetros (normas, tração, flecha, altura de fixação, transferência ao topo),
   dados mecânicos dos cabos (fibras, peso, diâmetro, maior vão e tração), resumo e a **tabela de esforço
   resultante em cada poste** (intensidade, ângulo, no topo, existente, total, nominal e resultado).
5. **Detalhamento de instalação** com as figuras A a E; a plaqueta traz logomarca, telefone, tipo de cabo e
   rota, a 300 mm do poste (a norma admite de 200 a 400 mm).
6. **Prazo e cronograma**, endereço da obra e responsável técnico, com assinatura.

É o conteúdo pedido no item 16.2 da NDU 009. Postes, CTO/CEO, cabos e esforços são levantados do desenho
(rode o **Esforço no Percurso** antes). Na janela você informa percurso (vai na capa e na plaqueta), endereço
da obra, local e data, **contrato de uso mútuo** (obrigatório na norma), **ART**, início e prazo da obra; esses
dados ficam gravados no DWG para a próxima vez. O PDF é gerado pelo Microsoft Edge (já vem no Windows),
sem abrir janela.

- **Dados da empresa**: `Dados/empresa.txt`, um campo por linha (`Campo: valor`); campo vazio não aparece.
  RG, CPF e endereço do representante são dados pessoais: preencha só na cópia local
  (`Documentos\Fiber Plugin\Dados\empresa.txt`, botão **Pasta de Dados**), nunca no repositório, que é público.
- **Logo e figuras**: `Dados/Memorial/` (`logo.png`, `fig-a-...` a `fig-e-...`). Para trocar, substitua o
  arquivo mantendo o nome.

## Google Earth (KML/KMZ)

**Importar KML** lê arquivos do Google Earth ou de programas de GIS (KML ou KMZ, qualquer pasta, inclusive
MultiGeometry) e converte latitude/longitude para a zona UTM do projeto (SIRGAS 2000/WGS84):
- **Pontos**: como **Ícones** (o mesmo símbolo do Google Earth: triângulo, círculo, quadrado, alvo, losango,
  hexágono, estrela, casa ou marcador, com a cor e o tamanho do KML), como **Postes** (modelo escolhido na lista,
  numerados em sequência, com texto de identificação e coordenadas; DT entra com rotação 0°) ou **Ignorar**.
  A forma vem do código do ícone do Google Earth web, do nome do arquivo do ícone ou, nos ícones embutidos
  no KML/KMZ, da análise do desenho do ícone; o que não for reconhecido entra como ponto (círculo com cruz).
- **Linhas**: como **Cabos** (tipo escolhido na lista, com nome e metragem vão a vão, prontos para o cálculo de
  esforço), como **Linhas** (polilinhas) ou **Ignorar**.
- **Polígonos**: polilinhas fechadas.
- **Layers**: uma por forma de ícone (`KML-TRIANGULO`, `KML-CIRCULO`, `KML-QUADRADO`...), `KML-LINHAS` e
  `KML-POLIGONOS`; linhas e polígonos levam a cor do KML. Os símbolos são blocos `KML_<FORMA>` criados no desenho.
- Sem zona definida no desenho, a do primeiro ponto do KML é gravada como zona do projeto. Coordenadas muito fora
  da zona geram aviso.

**Exportar KML** gera um KMZ com as pastas **Postes** (modelo, tipo, ID Energisa, esforço, resultado e UTM;
postes acima do nominal em vermelho), **CTO e CEO**, **Equipamentos Energisa** e **Cabos** (uma cor por tipo,
com metragem e número de vãos). Precisa da zona UTM do projeto; se não houver, ela é perguntada.

## Ruas do OpenStreetMap

**Importar Ruas** abre uma janela com todas as opções, baixa as ruas do OpenStreetMap (API Overpass, precisa de
internet) e desenha no Model já prontas, sem precisar de TRIM, BOUNDARY ou OVERKILL depois:
- **Forma da área**:
  - **Retângulo**: um número por lado, porque o retângulo é alinhado com o norte. Cada campo fica encostado no
    lado que define: Norte e Sul são latitudes (em UTM, valores de N), as linhas de cima e de baixo, em azul;
    Oeste e Leste são longitudes (em UTM, valores de E), as linhas da esquerda e da direita, em amarelo. Cada canto
    do desenho mostra, enquanto você digita, a latitude e a longitude que ele recebe.
  - **Centro e raio**: o quadrado em volta do ponto. Colar `-3.1019, -60.0250` (Google Maps) no campo da latitude
    preenche os dois campos.
  - **Marcar no desenho**: esconde a janela, você clica os dois cantos (ou o centro) e os campos se preenchem.
- **Coordenadas**: **Lat/Long** em graus decimais (`-3.1019`, `-3,1019`, `3.1019 S`, `60.025 W`) ou **UTM** em
  metros (`830710.50`, `9.656.678,58` e o formato dos textos do plugin, `405110.92 m E`). Trocar entre os dois
  converte o que já foi digitado.
- **Zona UTM**: a do projeto; sem ela, a digitada na janela (em lat/long pode ficar vazia: vem da longitude), que
  passa a ser a zona do projeto.
- A janela confere enquanto você digita e mostra o tamanho da área, ou o que está errado (lado trocado, UTM no
  campo de lat/long, área fora da zona, mais de 50 km). Os valores ficam gravados no DWG para a próxima vez.
- **Desenho**:
  - **Contorno** (padrão): o leito de todas as ruas unido, layer `RUA_CONTORNO`. Sai só o meio-fio: um contorno
    contínuo em volta de cada quadra, cruzamentos e rotatórias abertos, sem linhas sobrepostas. Vãos de menos de
    60 cm entre pistas são fechados.
  - **Eixo**: a linha do centro de cada via, numa layer por tipo (`RUA_RESIDENTIAL`, `RUA_PRIMARY`...).
  - **Ambos**: contorno e eixo.

  A largura vem da tag `width` do OSM, senão do número de faixas (3,3 m cada), senão do tipo de via
  (residencial 7 m, secundária 12 m...).
- Por padrão só as vias para veículos; calçadas, ciclovias e trilhas, se incluídas, têm o contorno à parte
  (`RUA_CAMINHOS`).
- **Nomes das ruas** (opção **Com os nomes das ruas**): o nome do OpenStreetMap (ou a sigla da rodovia, como
  `BR-174`, se ela não tiver nome) escrito na direção da rua e sempre de pé, layer `RUA_NOMES`, com 2,5 mm no papel
  (acompanha a **Escala**). Só com o contorno o nome fica no meio da rua; com o eixo, logo acima dele. Os trechos
  da mesma rua são juntados: cada rua recebe um nome, repetido a cada 400 m (em 1:1000) nas longas, e trechos
  mais curtos que o texto ficam sem nome.
- Tudo é cortado reto na borda da área.
- **Georreferenciado**: as coordenadas do desenho são as UTM do projeto, e o DWG recebe a geolocalização do
  AutoCAD (`SIRGAS2000.UTM-<zona>`, ou `UTM84-<zona>` se a biblioteca do AutoCAD não tiver o SIRGAS), em metros.
  Uma geolocalização que o desenho já tenha não é alterada (só avisa se a zona for outra).
- O servidor público do Overpass às vezes recusa por excesso de uso; o plugin tenta de novo e usa servidores
  reservas. Áreas com mais de 5 km de lado podem demorar.

Os dados são dos colaboradores do OpenStreetMap (licença ODbL) e a largura das ruas é aproximada: para projeto
executivo, confira com a base oficial do município.

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
   - coluna à direita com a **seta do norte** e a **legenda** em português dos símbolos usados no desenho
     (postes DT/CC, CTO, CEO, equipamentos, cabo projetado e seta de esforço), como pede o item 16.3 da NDU 009;
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

## Verificação com a NDU 009

O botão **Verificar Projeto** lista na linha de comando (e na aba *Verificação NDU 009* do relatório):

| Tipo | O que confere | Item da NDU 009 |
|---|---|---|
| Erro | Esforço total (projeto a 20 cm do topo + existente) acima do nominal do poste | 8.1 |
| Erro | Cabo em poste com trafo, chave, religador, regulador ou banco de capacitores | 8, nota I |
| Erro | Mais de uma CTO ou mais de uma CEO no mesmo poste | 17.4 nota IV, 17.5 g |
| Erro | CTO/CEO em poste com equipamento da Energisa ou para-raios | 17.5 c |
| Aviso | CTO/CEO em poste com deflexão de 45° ou mais (confira se é esquina) | 17.5 c |
| Erro | Cabos que somam mais de 1.680 kg/km ou 65 mm num ponto de fixação | 8 i |
| Erro | Vão em que, com flecha de 1%, o cabo fica abaixo da altura mínima ao solo | Tabela 02 |
| Aviso | Vão acima de 60 m (considerar vento e temperatura) | 16.3 |
| Aviso | Postes com cabo sem esforço calculado | 16.2 e |

Os equipamentos são os blocos do grupo Elétricos, ligados ao poste mais próximo (até 10 m). Não são
verificados: orientação dos postes DT, drops por vão e afastamentos da rede elétrica.

## Premissas de cálculo

**Esforço nos postes** (NDU 009, Anexo A)
- Tração de cada vão pela **Tabela 08 da NDU 009** (cabo de fibra óptica autossustentado, flecha de 1%, sem
  vento), por faixa de número de fibras e vão, com interpolação entre os vãos da tabela
  (`Dados/tracao_ndu009.csv`). O número de fibras vem da coluna `Fibras` da planilha de cabos ou do nome
  ("06F.O"). Em **Parâmetros** dá para usar o peso do cabo: `T = p·L² / (8·f) = 12,5 · p · L` (p em kgf/m).
- O esforço no poste é a soma vetorial das trações dos vãos que chegam nele (método analítico). De cada cabo é
  considerado o vértice mais próximo do poste, num raio de 2,5 m.
- O resultado é **transferido a 20 cm do topo**: `Ft = F · hc / h`, com altura útil `h = L − e` e engastamento
  `e = L/10 + 0,60 m`; `hc` é a altura de fixação do cabo (padrão 5,40 m). Num poste de 11 m, Ft = 0,58 · F.
- O **esforço existente** no poste (redes da Energisa e outras ocupantes) é somado ao do projeto antes de
  comparar com o nominal. A soma é direta (pior caso, os dois no mesmo sentido).
- O esforço nominal vem do modelo escolhido no `FIBRA_INSERIR_POSTE` (ou, em desenhos antigos, do nome do
  poste). Ele segue a norma em daN e é convertido para kgf: `DT 11/200` → 200 daN = 204 kgf.
- **Não considera** vento, variação de temperatura nem desnível entre postes (a Verificação avisa os vãos
  acima de 60 m). Para um laudo formal, confirme com a concessionária.

## Identificação dos elementos
Cabos, postes (inclusive esforço existente e ID Energisa), CTO/CEO e setas de esforço guardam seus dados em XData (aplicação `FIBRA_PLUGIN`), então
renomear layers não quebra os cálculos nem o relatório. Cabos desenhados por versões antigas do plugin
continuam sendo reconhecidos pelo nome da layer (`FIBRA_CABO_...`).
