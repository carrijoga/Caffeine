<#
.SYNOPSIS
    Builds and signs the Caffeine MSIX package for local installation.

.DESCRIPTION
    1. Builds the app as an MSIX (Release, x64 by default).
    2. Signs the produced .msix with the local self-signed dev certificate.

    The resulting .msix is written under:
        src/Caffeine.App/AppPackages/Caffeine.App_<version>_<platform>_Test/

.PARAMETER Platform
    x64 (default) or ARM64.

.NOTES
    Requires build/CaffeineDev.pfx (created by build/make-cert.ps1).
    To install the .msix, the CER must first be trusted once per machine
    (see build/make-cert.ps1 output / the install instructions).
#>
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$pfx = Join-Path $repoRoot 'build\CaffeineDev.pfx'
$pfxPassword = 'caffeine'
if (-not (Test-Path $pfx)) {
    throw "Signing certificate not found at $pfx. Run build\make-cert.ps1 first."
}

Write-Host "==> Building MSIX ($Configuration / $Platform)..." -ForegroundColor Cyan
dotnet build src/Caffeine.App/Caffeine.App.csproj `
    -c $Configuration -p:Platform=$Platform -p:GenerateAppxPackageOnBuild=true
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Locate the freshly built .msix
$msix = Get-ChildItem "src/Caffeine.App/AppPackages" -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No .msix produced." }

# Find signtool (newest SDK)
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK." }

Write-Host "==> Signing $($msix.Name)..." -ForegroundColor Cyan
& $signtool.FullName sign /fd SHA256 /a /f $pfx /p $pfxPassword $msix.FullName
if ($LASTEXITCODE -ne 0) { throw "Signing failed." }

Write-Host ""
Write-Host "Signed package ready:" -ForegroundColor Green
Write-Host "  $($msix.FullName)"
Write-Host ""
Write-Host "To install: double-click the .msix (after trusting the cert once)," -ForegroundColor Yellow
Write-Host "or run:  Add-AppxPackage '$($msix.FullName)'" -ForegroundColor Yellow
