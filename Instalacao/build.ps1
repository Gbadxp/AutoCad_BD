<#
.SYNOPSIS
    Gera o pacote de instalação do Fiber Plugin.

.DESCRIPTION
    1. Compila o plugin em Release (AutoCAD 2022-2024 e 2025-2026).
    2. Monta Instalacao\dist\FiberPlugin.bundle (DLLs + Dados + Blocos + PackageContents.xml).
    3. Opcional: assina as DLLs com um certificado de assinatura de código.
    4. Gera o instalador Instalacao\dist\FiberPlugin-<versão>.msi.

.PARAMETER Certificado
    Impressão digital (thumbprint) de um certificado de assinatura de código em
    Cert:\CurrentUser\My. Se informado, as DLLs são assinadas. Veja criar-certificado-teste.ps1.

.PARAMETER SemMsi
    Monta só a pasta .bundle, sem gerar o instalador .msi.

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Certificado 1A2B3C...
#>
param(
    [string]$Certificado,
    [switch]$SemMsi
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $root 'FiberPlugin\FiberPlugin.csproj'
$pluginDir = Join-Path $root 'FiberPlugin'
$dist      = Join-Path $PSScriptRoot 'dist'
$bundle    = Join-Path $dist 'FiberPlugin.bundle'
$contents  = Join-Path $bundle 'Contents'

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

Copy-Item (Join-Path $pluginDir 'bin\Release\net48\FiberPlugin.dll') (Join-Path $contents 'net48')
Copy-Item (Join-Path $pluginDir 'bin\Release\net8.0-windows\FiberPlugin.dll') (Join-Path $contents 'net8')

# Dados e Blocos padrão (copiados para Documentos\Fiber Plugin na primeira execução)
foreach ($folder in 'Dados', 'Blocos') {
    $source = Join-Path $pluginDir $folder
    $target = Join-Path $contents $folder
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem $source -Recurse -Directory | ForEach-Object {
        New-Item -ItemType Directory -Force (Join-Path $target $_.FullName.Substring($source.Length + 1)) | Out-Null
    }
    Get-ChildItem $source -Recurse -File | Where-Object { $_.Name -ne '.gitkeep' } | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $target $_.FullName.Substring($source.Length + 1))
    }
}
Copy-Item (Join-Path $PSScriptRoot 'LEIA-ME.txt') $contents

$manifest = (Get-Content (Join-Path $PSScriptRoot 'PackageContents.xml') -Raw -Encoding UTF8).Replace('$VERSION$', $version)
[IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))

# 3. Assinatura (opcional)
if ($Certificado) {
    Write-Host '> Assinando as DLLs...'
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe não encontrado (instale o Windows SDK).' }

    $dlls = Get-ChildItem $contents -Recurse -Filter 'FiberPlugin.dll' | ForEach-Object FullName
    & $signtool.FullName sign /sha1 $Certificado /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $dlls
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao assinar as DLLs.' }
}

# 4. Instalador .msi
if (-not $SemMsi) {
    Write-Host '> Gerando o instalador .msi...'
    $installer = Join-Path $PSScriptRoot 'Installer\FiberPlugin.Installer.wixproj'
    dotnet build $installer -c Release -nologo -v q "-p:Version=$version" "-p:BundleDir=$bundle"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o instalador.' }

    $msi = Get-ChildItem (Join-Path $PSScriptRoot 'Installer\bin') -Recurse -Filter "FiberPlugin-$version*.msi" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Copy-Item $msi.FullName (Join-Path $dist "FiberPlugin-$version.msi") -Force
}

Write-Host ''
Write-Host "Pronto! Arquivos em: $dist" -ForegroundColor Green
Get-ChildItem $dist | ForEach-Object { Write-Host "  $($_.Name)" }
