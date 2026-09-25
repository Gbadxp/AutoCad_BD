<#
.SYNOPSIS
    Instala o Fiber Plugin copiando o FiberPlugin.bundle para a pasta de plugins do AutoCAD.

.DESCRIPTION
    Destino: C:\Program Files\Autodesk\ApplicationPlugins\FiberPlugin.bundle
    Essa é uma pasta confiável do AutoCAD, então o plugin carrega sem aviso de segurança.
    Precisa de permissão de administrador (o script pede sozinho).

    Rode antes o build.ps1 (ou use o instalador .msi, que faz a mesma coisa).
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

$source = Join-Path $PSScriptRoot 'dist\FiberPlugin.bundle'
if (-not (Test-Path $source)) {
    throw "Pacote não encontrado em $source. Rode primeiro: .\build.ps1"
}

if (Restart-ComoAdministrador $PSCommandPath) { return }
Wait-AutoCADFechado 'instalar'

if (Test-Path $PastaPlugin) { Remove-Item $PastaPlugin -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path $PastaPlugin) | Out-Null
Copy-Item $source $PastaPlugin -Recurse

Write-Host "Fiber Plugin instalado em: $PastaPlugin" -ForegroundColor Green
Write-Host 'Abra o AutoCAD: a aba "Fibra" aparece na faixa de opções e o comando FIBRA abre o menu.'
Write-Host "Planilha de cabos e blocos: $([Environment]::GetFolderPath('MyDocuments'))\Fiber Plugin"
Read-Host 'Pressione Enter para fechar'
