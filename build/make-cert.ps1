<#
.SYNOPSIS
    Creates the self-signed code-signing certificate used to sign the MSIX.

.DESCRIPTION
    Generates build\CaffeineDev.pfx (for signing) and build\CaffeineDev.cer
    (to trust on each machine that will install the app).

    The certificate Subject MUST match the Publisher in Package.appxmanifest:
        CN=Gabriel Carrijo

    Run this once. The .pfx is gitignored — regenerate it on each dev machine.
#>
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$subject   = 'CN=Gabriel Carrijo'   # must equal Publisher in Package.appxmanifest
$pfxPath   = Join-Path $repoRoot 'build\CaffeineDev.pfx'
$cerPath   = Join-Path $repoRoot 'build\CaffeineDev.cer'
$pwdPlain  = 'caffeine'

$cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
    -KeyUsage DigitalSignature -FriendlyName 'Caffeine Dev Signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

$pwd = ConvertTo-SecureString -String $pwdPlain -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfxPath -Password $pwd | Out-Null
Export-Certificate  -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $cerPath | Out-Null

Write-Host "Certificate created." -ForegroundColor Green
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  PFX: $pfxPath  (password: $pwdPlain)"
Write-Host "  CER: $cerPath"
