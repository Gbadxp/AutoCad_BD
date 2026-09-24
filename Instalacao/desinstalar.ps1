<#
.SYNOPSIS
    Remove o Fiber Plugin instalado pelo instalar.ps1.

.DESCRIPTION
    Apaga C:\Program Files\Autodesk\ApplicationPlugins\FiberPlugin.bundle.
    Seus dados (Documentos\Fiber Plugin: planilha de cabos e blocos) NÃO são apagados.
    Se instalou pelo .msi, desinstale por Configurações > Aplicativos.
#>
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins\FiberPlugin.bundle'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

if (-not (Test-Path $target)) {
    Write-Host 'O Fiber Plugin não está instalado nesta pasta.'
}
else {
    if (Get-Process acad -ErrorAction SilentlyContinue) {
        Write-Warning 'O AutoCAD está aberto. Feche-o antes de desinstalar.'
        Read-Host 'Pressione Enter depois de fechar o AutoCAD'
    }
    Remove-Item $target -Recurse -Force
    Write-Host 'Fiber Plugin removido.' -ForegroundColor Green
    Write-Host "Seus dados continuam em: $([Environment]::GetFolderPath('MyDocuments'))\Fiber Plugin"
}
Read-Host 'Pressione Enter para fechar'
