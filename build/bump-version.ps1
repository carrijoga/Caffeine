<#
.SYNOPSIS
    Bumps the patch component of the MSIX package version in Package.appxmanifest.

.DESCRIPTION
    Reads Identity/@Version (format A.B.C.D), increments C by 1, writes the
    manifest back in place. Prints "NEW_VERSION=<version>" as the last line
    of output so callers (CI) can capture the new version.
#>
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'src\Caffeine.App\Package.appxmanifest'

[xml]$manifest = Get-Content -Path $manifestPath -Raw

$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('a', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identityNode = $manifest.SelectSingleNode('//a:Package/a:Identity', $ns)

$oldVersion = $identityNode.Version
$parts = $oldVersion.Split('.')
if ($parts.Count -ne 4) {
    throw "Expected version in A.B.C.D format, got '$oldVersion'"
}

$parts[2] = [string]([int]$parts[2] + 1)
$newVersion = $parts -join '.'

$identityNode.Version = $newVersion
$manifest.Save($manifestPath)

Write-Host "Bumped version: $oldVersion -> $newVersion"
Write-Output "NEW_VERSION=$newVersion"
