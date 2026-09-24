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

$source = Join-Path $PSScriptRoot 'dist\FiberPlugin.bundle'
$target = Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins\FiberPlugin.bundle'

if (-not (Test-Path $source)) {
    throw "Pacote não encontrado em $source. Rode primeiro: .\build.ps1"
}

# Reabre como administrador se necessário
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

if (Get-Process acad -ErrorAction SilentlyContinue) {
    Write-Warning 'O AutoCAD está aberto. Feche-o antes de instalar (a DLL fica bloqueada enquanto ele roda).'
    Read-Host 'Pressione Enter depois de fechar o AutoCAD'
}

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
Copy-Item $source $target -Recurse

Write-Host "Fiber Plugin instalado em: $target" -ForegroundColor Green
Write-Host 'Abra o AutoCAD: a aba "Fibra" aparece na faixa de opções e o comando FIBRA abre o menu.'
Write-Host "Planilha de cabos e blocos: $([Environment]::GetFolderPath('MyDocuments'))\Fiber Plugin"
Read-Host 'Pressione Enter para fechar'
