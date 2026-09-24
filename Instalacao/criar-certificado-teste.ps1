<#
.SYNOPSIS
    Cria um certificado de assinatura de código AUTOASSINADO para testes / uso interno.

.DESCRIPTION
    Instalando pelo instalar.ps1 ou pelo .msi (Program Files), a assinatura NÃO é necessária:
    essa pasta já é confiável para o AutoCAD.

    A assinatura serve para carregar a DLL de outras pastas sem o aviso do SECURELOAD.
    Um certificado autoassinado só é aceito nos computadores onde ele for marcado como confiável.
    Para distribuir para outras empresas, compre um certificado de assinatura de código de uma
    autoridade certificadora (DigiCert, Sectigo, etc.) e use o thumbprint dele no build.ps1.

    ATENÇÃO: este script altera o repositório de certificados do Windows do seu usuário.

.EXAMPLE
    .\criar-certificado-teste.ps1
    .\build.ps1 -Certificado <thumbprint mostrado>
#>
$ErrorActionPreference = 'Stop'

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject 'CN=Fiber Plugin (teste)' `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(5)

$cerFile = Join-Path $PSScriptRoot 'FiberPlugin-teste.cer'
Export-Certificate -Cert $cert -FilePath $cerFile | Out-Null

Write-Host "Certificado criado. Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
Write-Host ''
Write-Host 'Para o Windows e o AutoCAD confiarem nele, importe o arquivo abaixo em'
Write-Host '"Autoridades de Certificação Raiz Confiáveis" e "Fornecedores Confiáveis" de cada computador:'
Write-Host "  $cerFile"
Write-Host ''
Write-Host "Depois gere o pacote assinado com:  .\build.ps1 -Certificado $($cert.Thumbprint)"
