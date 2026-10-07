<#
.SYNOPSIS
    Gera o instalador do Fiber Plugin: Instalador\FiberPlugin-<versão>.msi.

.DESCRIPTION
    1. Compila o plugin em Release (AutoCAD 2022-2024 e 2025-2026).
    2. Monta o FiberPlugin.bundle (DLLs + Dados + Blocos + Normas + PackageContents.xml + README) numa pasta
       temporária de compilação.
    3. Opcional: assina as DLLs com um certificado de assinatura de código.
    4. Gera o .msi na pasta Instalador, apagando os de versões anteriores (fica só a versão atual).

.PARAMETER Certificado
    Impressão digital (thumbprint) de um certificado de assinatura de código em
    Cert:\CurrentUser\My. Se informado, as DLLs são assinadas. Veja criar-certificado-teste.ps1.

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Certificado 1A2B3C...
#>
param(
    [string]$Certificado
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$pluginDir = Join-Path $root 'FiberPlugin'
$project   = Join-Path $pluginDir 'FiberPlugin.csproj'
$installer = Join-Path $PSScriptRoot 'Installer\FiberPlugin.Installer.wixproj'
$bundle    = Join-Path $PSScriptRoot 'Installer\obj\FiberPlugin.bundle'
$contents  = Join-Path $bundle 'Contents'
$output    = Join-Path $root 'Instalador'

# Versão definida no FiberPlugin.csproj
[xml]$csproj = Get-Content $project -Raw
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Não encontrei <Version> no FiberPlugin.csproj.' }
Write-Host "Fiber Plugin v$version" -ForegroundColor Cyan

# 1. Compilação
Write-Host '> Compilando em Release...'
dotnet build $project -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }

# 2. Pacote .bundle
Write-Host '> Montando FiberPlugin.bundle...'
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $contents 'net48'), (Join-Path $contents 'net8') | Out-Null

# O plugin e as bibliotecas que ele usa (Clipper2Lib.dll: contorno das ruas)
foreach ($dll in 'FiberPlugin.dll', 'Clipper2Lib.dll') {
    Copy-Item (Join-Path $pluginDir "bin\Release\net48\$dll") (Join-Path $contents 'net48')
    Copy-Item (Join-Path $pluginDir "bin\Release\net8.0-windows\$dll") (Join-Path $contents 'net8')
}

# Dados (com a subpasta Memorial), Blocos e Normas (NDU 009 em PDF), sem os temporários e backups que o AutoCAD cria ao salvar
$temporarios = '.bak', '.dwl', '.dwl2', '.tmp', '.sv$'
foreach ($folder in 'Dados', 'Blocos', 'Normas') {
    $source = Join-Path $pluginDir $folder
    Get-ChildItem $source -Recurse -File |
        Where-Object { $_.Name -notlike '~*' -and $temporarios -notcontains $_.Extension } |
        ForEach-Object {
            $target = Join-Path (Join-Path $contents $folder) $_.FullName.Substring($source.Length + 1)
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            Copy-Item $_.FullName $target
        }
}
Copy-Item (Join-Path $root 'README.md') $contents

$manifest = (Get-Content (Join-Path $PSScriptRoot 'PackageContents.xml') -Raw -Encoding UTF8).Replace('$VERSION$', $version)
[IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))

# 3. Assinatura (opcional)
if ($Certificado) {
    Write-Host '> Assinando as DLLs...'
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe não encontrado (instale o Windows SDK).' }

    $dlls = Get-ChildItem $contents -Recurse -Filter '*.dll' | ForEach-Object FullName
    & $signtool.FullName sign /sha1 $Certificado /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $dlls
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao assinar as DLLs.' }
}

# 4. Instalador .msi, direto na pasta Instalador (só a versão atual)
Write-Host '> Gerando o instalador .msi...'
dotnet build $installer -c Release -nologo -v q "-p:Version=$version" "-p:BundleDir=$bundle"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o instalador.' }

$msi = Get-ChildItem (Join-Path $PSScriptRoot 'Installer\bin') -Recurse -Filter "FiberPlugin-$version.msi" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
New-Item -ItemType Directory -Force $output | Out-Null
Get-ChildItem $output -Filter 'FiberPlugin-*.msi' | Remove-Item -Force
Copy-Item $msi.FullName $output

Write-Host ''
Write-Host "Pronto! $(Join-Path $output $msi.Name)" -ForegroundColor Green
