# Plugin para AutoCAD - Projetos de Fibra Óptica

Plugin para AutoCAD que auxilia em projetos de redes de fibra óptica (FTTH/FTTx).
Compatível com **AutoCAD 2022, 2023, 2024, 2025 e 2026** (inclusive verticais como Map 3D e Civil 3D).

## Tecnologias
- Linguagem: C#
- Duas compilações do mesmo código:
  - `net48` (.NET Framework 4.8, pacote `AutoCAD.NET` 24.1) → AutoCAD 2022, 2023 e 2024
  - `net8.0-windows` (.NET 8, pacote `AutoCAD.NET` 25.1.0) → AutoCAD 2025 e 2026

## Instalação

### Opção 1: instalador (recomendado)
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
- **Seus dados ficam em `Documentos\Fiber Plugin`** (Dados e Blocos), que é editável sem permissão
  de administrador. A pasta é criada na primeira execução com o conteúdo padrão. Atualizar ou
  desinstalar o plugin não apaga nada dela.

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
│   ├── perdas_opticas.csv   perdas usadas no budget óptico
│   └── bom_ignorar.txt      blocos que não entram na lista de materiais
└── Blocos/     biblioteca de blocos (.dwg), uma subpasta por categoria
    ├── Fibra/  Eletrica/  Postes/  Outros/
    └── LEIA-ME.txt
```

**Dados**: as planilhas são lidas a cada comando. Basta editar no Excel e salvar, sem recompilar.
Aceita CSV salvo pelo Excel em português (separador `;`, vírgula decimal).

**Blocos**: cada `.dwg` é um bloco com o nome do arquivo, e a subpasta define a categoria.
Quando um comando precisa de um bloco que não está no desenho, ele é importado da pasta
automaticamente, então **não é mais preciso partir do template**. Para montar a biblioteca a partir
do template atual, abra o `TEMPLATE_CAD/TEMPLATE_BD_V1.dwg` e rode `FIBRA_EXPORTAR_BLOCOS`.

Instalado, o plugin usa `Documentos\Fiber Plugin\Dados` e `\Blocos`. Em desenvolvimento, usa as
pastas do código-fonte.

## Comandos Disponíveis

| Comando | Função |
|---|---|
| `FIBRA` | Menu principal com todas as ferramentas. |
| `FIBRA_LANCAR_CABO` | Desenha o cabo clicando nos pontos; escreve nome e metragem vão a vão. |
| `FIBRA_ROTEAMENTO_AUTO` | Seleciona blocos e gera a rota mais curta entre eles (vizinho mais próximo testando todos os inícios + otimização 2-opt), afastada 1,8 m dos postes. |
| `FIBRA_INSERIR_POSTE` | Insere postes numerados em sequência, continuando do maior número já existente no desenho. |
| `FIBRA_INSERIR_BLOCO` | Insere CTO, CEO e outros blocos, do desenho ou da pasta Blocos. |
| `FIBRA_NOMEAR_POSTE` | Grava o tipo do poste (ex.: `DT 11/200`) nos blocos clicados. |
| `FIBRA_NUMERAR_PONTOS` | Marca pontos com numeração P01, P02... e coordenadas X/Y. |
| `FIBRA_CALCULAR_ESFORCO` | Esforço de um único cabo numa sequência de postes clicados (fora do menu). |
| `FIBRA_ESFORCO_TOTAL` | Esforço resultante de todos os cabos num poste, comparado com o nominal. |
| `FIBRA_ESFORCO_PERCURSO` | Coloca a seta de esforço em todos os postes do percurso dos cabos selecionados (total no poste ou só o cabo). |
| `FIBRA_RELATORIO_ESFORCOS` | CSV com o esforço (kgf) de todos os postes, nominal, % de utilização e situação (OK/EXCEDIDO). |
| `FIBRA_EXPORTAR_CSV` | Lista de materiais (blocos + metragem por cabo) e coordenadas. |
| `FIBRA_CALCULAR_BOBINAS` | Quantidade de bobinas por tipo de cabo, com margem de segurança. |
| `FIBRA_BUDGET_OPTICO` | Perda do enlace (fibra, emendas, conectores, splitters) comparada com a classe B+/C+. |
| `FIBRA_EXPORTAR_BLOCOS` | Salva os blocos do desenho aberto na pasta Blocos. |
| `FIBRA_ABRIR_PASTA` | Abre a pasta com a planilha de cabos e a biblioteca de blocos. |
| `FIBRA_RIBBON` | Recria a aba "Fibra" (se ela sumir após trocar de espaço de trabalho). |
| `FIBRA_SOBRE` | Mostra a versão instalada e as pastas em uso. |

## Premissas de cálculo

**Esforço nos postes**
- Tração de cada vão pela aproximação da parábola, com flecha de 1% do vão:
  `T = p·L² / (8·f) = 12,5 · p · L` (p em kgf/m), em **kgf** (a unidade usada em todo o plugin).
- O esforço no poste é a soma vetorial das trações dos vãos que chegam nele. De cada cabo é
  considerado o vértice mais próximo do poste, num raio de 2,5 m.
- **Não considera** vento, variação de temperatura, desnível entre postes nem a tração máxima de
  projeto do fabricante do cabo. Para um laudo formal, confirme com a norma da concessionária.
- O esforço nominal é lido do nome do poste, que segue a norma em daN, e convertido para kgf:
  `DT 11/200` → 200 daN = 204 kgf.

**Budget óptico**: valores típicos em `Dados/perdas_opticas.csv` (0,35 dB/km, emenda 0,1 dB,
conector 0,5 dB, splitters PLC, classes GPON B+ 13–28 dB e C+ 17–32 dB, margem 3 dB). Ajuste
para os valores do seu fornecedor.

## Identificação dos cabos
Cada cabo desenhado guarda o tipo em XData (aplicação `FIBRA_PLUGIN`), então renomear a layer não
quebra os cálculos. Cabos desenhados por versões antigas do plugin continuam sendo reconhecidos
pelo nome da layer (`FIBRA_CABO_...`).
