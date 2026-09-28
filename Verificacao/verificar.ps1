<#
.SYNOPSIS
    Verifica se o computador tem tudo o que o Fiber Plugin precisa e oferece instalar o que faltar.

.DESCRIPTION
    Para usar o plugin:
      - Windows 64 bits
      - AutoCAD 2022, 2023, 2024, 2025 ou 2026
      - .NET Framework 4.8          (AutoCAD 2022 a 2024)
      - .NET 8 Desktop Runtime      (AutoCAD 2025 e 2026)
      - Fiber Plugin instalado e atualizado
    Para editar e compilar o código (opcional):
      - .NET SDK 8 ou mais novo
      - Git

    Para cada item que faltar, pergunta se quer instalar (via winget ou pelo instalador do plugin).

.PARAMETER SomenteVerificar
    Só mostra o resultado, sem perguntar nada nem instalar.

.PARAMETER Desenvolvimento
    Verifica também as ferramentas para compilar o código, sem perguntar.

.EXAMPLE
    .\verificar.ps1
.EXAMPLE
    .\verificar.ps1 -SomenteVerificar -Desenvolvimento
#>
param(
    [switch]$SomenteVerificar,
    [switch]$Desenvolvimento
)

$ErrorActionPreference = 'Continue'
$raiz = Split-Path -Parent $PSScriptRoot
$pastaPlugin = Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins\FiberPlugin.bundle'
$paginaGitHub = 'https://github.com/Gbadxp/AutoCad_BD'

# Versões suportadas: série interna do AutoCAD → ano e .NET usado pelo plugin
$versoesAutoCad = [ordered]@{
    'R24.1' = @{ Ano = 2022; Net = 'net48' }
    'R24.2' = @{ Ano = 2023; Net = 'net48' }
    'R24.3' = @{ Ano = 2024; Net = 'net48' }
    'R25.0' = @{ Ano = 2025; Net = 'net8' }
    'R25.1' = @{ Ano = 2026; Net = 'net8' }
}

# Itens que faltam: Nome, Detalhe e Instalar (bloco de código; $null = o usuário resolve manualmente)
$faltando = New-Object System.Collections.ArrayList

# ---------- Saída ----------

function Write-Ok([string]$Texto)    { Write-Host '  [OK]    ' -ForegroundColor Green -NoNewline; Write-Host $Texto }
function Write-Falta([string]$Texto) { Write-Host '  [FALTA] ' -ForegroundColor Red -NoNewline; Write-Host $Texto }
function Write-Aviso([string]$Texto) { Write-Host '  [AVISO] ' -ForegroundColor Yellow -NoNewline; Write-Host $Texto }
function Write-Info([string]$Texto)  { Write-Host "          $Texto" -ForegroundColor DarkGray }
function Write-Secao([string]$Texto) { Write-Host ''; Write-Host $Texto -ForegroundColor Cyan }

function Add-Falta([string]$Nome, [string]$Detalhe, [scriptblock]$Instalar) {
    Write-Falta "$Nome - $Detalhe"
    [void]$faltando.Add([pscustomobject]@{ Nome = $Nome; Detalhe = $Detalhe; Instalar = $Instalar })
}

function Read-Sim([string]$Pergunta, [bool]$Padrao) {
    $opcoes = 's/N'
    if ($Padrao) { $opcoes = 'S/n' }
    $resposta = (Read-Host "$Pergunta [$opcoes]").Trim().ToLowerInvariant()
    if ($resposta -eq '') { return $Padrao }
    return $resposta.StartsWith('s')
}

# ---------- Instalação ----------

function Test-Winget { return [bool](Get-Command winget -ErrorAction SilentlyContinue) }

<# Instala pelo winget; sem winget, abre a página de download. #>
function Install-Pacote([string]$Id, [string]$PaginaDownload) {
    if (Test-Winget) {
        Write-Host "  > winget install --id $Id" -ForegroundColor DarkGray
        winget install --id $Id --exact --source winget | Out-Host
        return ($LASTEXITCODE -eq 0)
    }
    Write-Aviso 'winget não encontrado. Abrindo a página de download...'
    Start-Process $PaginaDownload
    return $false
}

function Wait-AutoCadFechado {
    while (Get-Process acad -ErrorAction SilentlyContinue) {
        Write-Aviso 'O AutoCAD está aberto. Feche-o para instalar o plugin.'
        Read-Host '          Pressione Enter depois de fechar o AutoCAD' | Out-Null
    }
}

# ---------- Verificações ----------

function Get-VersaoPluginInstalada {
    $manifesto = Join-Path $pastaPlugin 'PackageContents.xml'
    if (-not (Test-Path $manifesto)) { return $null }
    try {
        [xml]$xml = Get-Content $manifesto -Raw -Encoding UTF8
        return [version]$xml.ApplicationPackage.AppVersion
    } catch {
        return [version]'0.0'
    }
}

<# Instalador mais novo na pasta Instalador do repositório (null se não houver). #>
function Get-InstaladorDisponivel {
    $pasta = Join-Path $raiz 'Instalador'
    if (-not (Test-Path $pasta)) { return $null }

    $melhor = $null
    foreach ($arquivo in Get-ChildItem $pasta -Filter 'FiberPlugin-*.msi') {
        $texto = $arquivo.BaseName.Substring('FiberPlugin-'.Length)
        $versao = $null
        if (-not [version]::TryParse($texto, [ref]$versao)) { continue }
        if ($melhor -eq $null -or $versao -gt $melhor.Versao) {
            $melhor = [pscustomobject]@{ Caminho = $arquivo.FullName; Versao = $versao }
        }
    }
    return $melhor
}

function Test-Windows {
    Write-Secao 'Windows'
    $so = (Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption
    if ([Environment]::Is64BitOperatingSystem) {
        Write-Ok "$so (64 bits)"
    } else {
        Add-Falta 'Windows 64 bits' 'o AutoCAD e o plugin só funcionam em Windows 64 bits.' $null
    }
}

<# Retorna a lista de AutoCADs suportados instalados (Ano, Net, Pasta). #>
function Test-AutoCad {
    Write-Secao 'AutoCAD'
    $encontrados = New-Object System.Collections.ArrayList
    $base = 'HKLM:\SOFTWARE\Autodesk\AutoCAD'

    if (Test-Path $base) {
        foreach ($serie in Get-ChildItem $base -ErrorAction SilentlyContinue) {
            $nomeSerie = $serie.PSChildName
            foreach ($produto in Get-ChildItem $serie.PSPath -ErrorAction SilentlyContinue) {
                $props = Get-ItemProperty $produto.PSPath -ErrorAction SilentlyContinue
                if (-not $props -or -not $props.AcadLocation) { continue }
                if (-not (Test-Path (Join-Path $props.AcadLocation 'acad.exe'))) { continue }

                $nome = $props.ProductName
                if (-not $nome) { $nome = "AutoCAD ($nomeSerie)" }

                if ($versoesAutoCad.Contains($nomeSerie)) {
                    $info = $versoesAutoCad[$nomeSerie]
                    Write-Ok "$nome"
                    Write-Info $props.AcadLocation
                    [void]$encontrados.Add([pscustomobject]@{ Ano = $info.Ano; Net = $info.Net; Pasta = $props.AcadLocation })
                } else {
                    Write-Aviso "$nome - versão não suportada pelo plugin (use 2022 a 2026)."
                }
            }
        }
    }

    if ($encontrados.Count -eq 0) {
        Add-Falta 'AutoCAD 2022 a 2026' 'nenhum AutoCAD compatível encontrado. Instale pela sua conta Autodesk (manage.autodesk.com).' $null
    }
    return ,$encontrados
}

function Test-NetFramework([bool]$Necessario) {
    Write-Secao '.NET Framework 4.8 (AutoCAD 2022 a 2024)'
    $release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
    if ($release -ge 528040) {
        Write-Ok '.NET Framework 4.8 ou mais novo instalado'
    } elseif ($Necessario) {
        Add-Falta '.NET Framework 4.8' 'necessário para o plugin no AutoCAD 2022 a 2024.' {
            Install-Pacote 'Microsoft.DotNet.Framework.DeveloperPack_4' 'https://dotnet.microsoft.com/download/dotnet-framework/net48'
        }
    } else {
        Write-Info 'Não instalado, mas só é preciso para o AutoCAD 2022 a 2024.'
    }
}

function Test-DesktopRuntime8([bool]$Necessario) {
    Write-Secao '.NET 8 Desktop Runtime (AutoCAD 2025 e 2026)'
    $pasta = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
    $versoes = @()
    if (Test-Path $pasta) {
        $versoes = @(Get-ChildItem $pasta -Directory | Where-Object { $_.Name -like '8.*' } | ForEach-Object { [version]$_.Name } | Sort-Object)
    }

    if ($versoes.Count -gt 0) {
        Write-Ok ".NET 8 Desktop Runtime $($versoes[-1])"
    } elseif ($Necessario) {
        Add-Falta '.NET 8 Desktop Runtime' 'necessário para o plugin no AutoCAD 2025 e 2026.' {
            Install-Pacote 'Microsoft.DotNet.DesktopRuntime.8' 'https://dotnet.microsoft.com/download/dotnet/8.0'
        }
    } else {
        Write-Info 'Não instalado, mas só é preciso para o AutoCAD 2025 e 2026.'
    }
}

function Test-Plugin {
    Write-Secao 'Fiber Plugin'
    $instalada = Get-VersaoPluginInstalada
    $disponivel = Get-InstaladorDisponivel

    $script:msiPlugin = $null
    if ($disponivel) { $script:msiPlugin = $disponivel.Caminho }
    $instalar = {
        if (-not $script:msiPlugin) {
            Write-Aviso 'Instalador não encontrado na pasta Instalador. Abrindo o GitHub...'
            Start-Process "$paginaGitHub/tree/main/Instalador"
            return $false
        }
        Wait-AutoCadFechado
        Write-Host "  > $script:msiPlugin" -ForegroundColor DarkGray
        $p = Start-Process msiexec.exe -ArgumentList '/i', "`"$script:msiPlugin`"" -Wait -PassThru
        return ($p.ExitCode -eq 0)
    }

    if ($instalada -eq $null) {
        Add-Falta 'Fiber Plugin' 'não está instalado.' $instalar
    } else {
        $dlls = @(@('Contents\net48\FiberPlugin.dll', 'Contents\net8\FiberPlugin.dll') |
            Where-Object { -not (Test-Path (Join-Path $pastaPlugin $_)) })

        if ($dlls.Count -gt 0) {
            Add-Falta 'Fiber Plugin' "instalação incompleta (falta $($dlls -join ', ')). Reinstale." $instalar
        } elseif ($disponivel -and $disponivel.Versao -gt $instalada) {
            Add-Falta 'Fiber Plugin' "versão $instalada instalada; a $($disponivel.Versao) está disponível." $instalar
        } else {
            Write-Ok "Fiber Plugin $instalada instalado"
        }
        Write-Info $pastaPlugin
    }

    $dados = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Fiber Plugin'
    if (Test-Path $dados) {
        Write-Ok "Pasta de dados: $dados"
    } else {
        Write-Info "A pasta de dados ($dados) é criada ao abrir o AutoCAD com o plugin."
    }
}

function Test-Desenvolvimento {
    Write-Secao 'Ferramentas para editar e compilar (opcional)'

    $sdks = @()
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $sdks = @(dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] } |
            Where-Object { $_ -match '^\d+\.\d+\.\d+$' -and [int](($_ -split '\.')[0]) -ge 8 } |
            ForEach-Object { [version]$_ } | Sort-Object)
    }
    if ($sdks.Count -gt 0) {
        Write-Ok ".NET SDK $($sdks[-1])"
    } else {
        Add-Falta '.NET SDK 8' 'necessário para compilar o plugin e gerar o instalador (build.ps1).' {
            Install-Pacote 'Microsoft.DotNet.SDK.8' 'https://dotnet.microsoft.com/download/dotnet/8.0'
        }
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        Write-Ok ((git --version) -replace '^git version ', 'Git ')
    } else {
        Add-Falta 'Git' 'necessário para baixar e enviar o código ao GitHub.' {
            Install-Pacote 'Git.Git' 'https://git-scm.com/download/win'
        }
    }

    if (Test-Winget) { Write-Ok 'winget (instalador de programas do Windows)' }
    else { Write-Aviso 'winget não encontrado: as instalações vão abrir a página de download.' }
}

# ---------- Execução ----------

Write-Host ''
Write-Host '  Fiber Plugin - Verificação do computador' -ForegroundColor Cyan
Write-Host '  ========================================' -ForegroundColor Cyan

Test-Windows
$autocads = Test-AutoCad

# Sem AutoCAD encontrado, verifica os dois .NET (o usuário pode instalar qualquer versão depois)
$precisaNet48 = ($autocads.Count -eq 0) -or [bool]($autocads | Where-Object { $_.Net -eq 'net48' })
$precisaNet8  = ($autocads.Count -eq 0) -or [bool]($autocads | Where-Object { $_.Net -eq 'net8' })
Test-NetFramework $precisaNet48
Test-DesktopRuntime8 $precisaNet8
Test-Plugin

if (-not $Desenvolvimento -and -not $SomenteVerificar) {
    Write-Host ''
    $Desenvolvimento = Read-Sim 'Verificar também as ferramentas para editar e compilar o código (.NET SDK, Git)?' $false
}
if ($Desenvolvimento) { Test-Desenvolvimento }

Write-Secao 'Resultado'
if ($faltando.Count -eq 0) {
    Write-Ok 'Tudo pronto: o Fiber Plugin tem o que precisa neste computador.'
} else {
    Write-Host "  Falta(m) $($faltando.Count) item(ns):" -ForegroundColor Yellow
    foreach ($item in $faltando) { Write-Host "   - $($item.Nome): $($item.Detalhe)" }

    if (-not $SomenteVerificar) {
        $instalouAlgo = $false
        foreach ($item in $faltando) {
            Write-Host ''
            if ($item.Instalar -eq $null) {
                Write-Aviso "$($item.Nome): precisa ser resolvido manualmente."
                continue
            }
            if (Read-Sim "Instalar $($item.Nome) agora?" $true) {
                $ok = & $item.Instalar
                if ($ok) { Write-Ok "$($item.Nome) instalado."; $instalouAlgo = $true }
                else { Write-Aviso "$($item.Nome): a instalação não foi concluída." }
            }
        }
        if ($instalouAlgo) {
            Write-Host ''
            Write-Host '  Rode esta verificação de novo para confirmar (feche e abra a janela antes).' -ForegroundColor Cyan
        }
    }
}
Write-Host ''
