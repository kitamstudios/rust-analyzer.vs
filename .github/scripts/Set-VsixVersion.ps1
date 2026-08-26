#Requires -PSEdition Core
#Requires -Version 7.1

# The one process allowed to write the VSIX version (Ruling S; golden rule #5). The version field of
# src/RustAnalyzer/source.extension.cs is an auto-stamped generated value, so this script is the
# documented process that stamps it — it is never hand-edited.
#
# The manifest, the generated constant and the version this script reports must never disagree: publish
# tags the release with what it reports, so all three come from one computed value.
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $BuildNumber
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$manifestPath = Join-Path $repoRoot "src\RustAnalyzer\source.extension.vsixmanifest"
$sourcePath = Join-Path $repoRoot "src\RustAnalyzer\source.extension.cs"
$identityPattern = '<Identity[^>]*?\sVersion="(?<version>[^"]*)"'
$constantPattern = 'public const string Version = "(?<version>[^"]*)"'

function Set-VersionInFile {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Version
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, $Pattern)
    if (-not $match.Success) {
        throw "No version was found in $Path."
    }

    # Surgical replacement of the captured group, not an XML or source round-trip: nothing else in
    # either file may move.
    $group = $match.Groups["version"]
    [IO.File]::WriteAllText(
        $Path,
        $content.Remove($group.Index, $group.Length).Insert($group.Index, $Version))
}

# The manifest carries the base version; the build number is what distinguishes one build of it.
$identity = [regex]::Match((Get-Content -LiteralPath $manifestPath -Raw), $identityPattern)
if (-not $identity.Success) {
    throw "No Identity/@Version was found in $manifestPath."
}

$version = "$($identity.Groups["version"].Value).$BuildNumber"
Set-VersionInFile -Path $manifestPath -Pattern $identityPattern -Version $version
Set-VersionInFile -Path $sourcePath -Pattern $constantPattern -Version $version

Write-Host "VSIX version: $version"

return $version
