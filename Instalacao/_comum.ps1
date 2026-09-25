# Funções compartilhadas pelo instalar.ps1 e desinstalar.ps1.

# Pasta do Autoloader onde o plugin é instalado (confiável para o AutoCAD, sem aviso de segurança)
$PastaPlugin = Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins\FiberPlugin.bundle'

<#
  Reabre o script como administrador, se ainda não for.
  Retorna $true quando reabriu: o script original deve encerrar logo em seguida.
#>
function Restart-ComoAdministrador([string]$Script) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { return $false }

    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$Script`""
    return $true
}

# A DLL fica bloqueada enquanto o AutoCAD está aberto
function Wait-AutoCADFechado([string]$Acao) {
    if (Get-Process acad -ErrorAction SilentlyContinue) {
        Write-Warning "O AutoCAD está aberto. Feche-o antes de $Acao."
        Read-Host 'Pressione Enter depois de fechar o AutoCAD'
    }
}
