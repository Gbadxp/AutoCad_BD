<#
.SYNOPSIS
    Remove o Fiber Plugin instalado pelo instalar.ps1.

.DESCRIPTION
    Apaga C:\Program Files\Autodesk\ApplicationPlugins\FiberPlugin.bundle.
    Seus dados (Documentos\Fiber Plugin: planilha de cabos e blocos) NÃO são apagados.
    Se instalou pelo .msi, desinstale por Configurações > Aplicativos.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

if (Restart-ComoAdministrador $PSCommandPath) { return }

if (-not (Test-Path $PastaPlugin)) {
    Write-Host 'O Fiber Plugin não está instalado nesta pasta.'
}
else {
    Wait-AutoCADFechado 'desinstalar'
    Remove-Item $PastaPlugin -Recurse -Force
    Write-Host 'Fiber Plugin removido.' -ForegroundColor Green
    Write-Host "Seus dados continuam em: $([Environment]::GetFolderPath('MyDocuments'))\Fiber Plugin"
}
Read-Host 'Pressione Enter para fechar'
