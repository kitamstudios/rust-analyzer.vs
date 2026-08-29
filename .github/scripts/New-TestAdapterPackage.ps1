#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string] $OutputDirectory,
    [Parameter(Mandatory)]
    [string] $DestinationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$canonicalOutputDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "_built\projects\RustAnalyzer.TestAdapter"))
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$resolvedDestinationPath = [IO.Path]::GetFullPath($DestinationPath, $repoRoot)

if (-not $resolvedOutputDirectory.Equals(
        $canonicalOutputDirectory,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "The TestAdapter output directory must be the canonical owner path: $canonicalOutputDirectory."
}

$destinationDirectory = Split-Path -Parent $resolvedDestinationPath
if (-not $destinationDirectory.Equals(
        $canonicalOutputDirectory,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "The TestAdapter ZIP must be written to its canonical owner directory: $canonicalOutputDirectory."
}

$packageFiles = @(
    & (Join-Path $PSScriptRoot "Get-TestAdapterPackageFile.ps1") `
        -OutputDirectory $resolvedOutputDirectory)
$missingFiles = @(
    $packageFiles |
        Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missingFiles.Count -ne 0) {
    throw "The TestAdapter package input is missing or is not a file: $($missingFiles -join ", ")."
}

$emptyFiles = @(
    $packageFiles |
        Where-Object { (Get-Item -LiteralPath $_).Length -eq 0 })
if ($emptyFiles.Count -ne 0) {
    throw "The TestAdapter package input is empty: $($emptyFiles -join ", ")."
}

$entryNames = @($packageFiles | ForEach-Object { Split-Path -Leaf $_ })
$duplicateEntryNames = @(
    $entryNames |
        Group-Object |
        Where-Object Count -gt 1 |
        ForEach-Object Name)
if ($duplicateEntryNames.Count -ne 0) {
    throw "The TestAdapter package file list contains duplicate destination names: $($duplicateEntryNames -join ", ")."
}

Compress-Archive -LiteralPath $packageFiles -DestinationPath $resolvedDestinationPath -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedDestinationPath)
try {
    $actualEntries = @($archive.Entries.FullName)
    $duplicateArchiveEntries = @(
        $actualEntries |
            Group-Object |
            Where-Object Count -gt 1 |
            ForEach-Object Name)
    if ($duplicateArchiveEntries.Count -ne 0) {
        throw "The TestAdapter ZIP contains duplicate entries: $($duplicateArchiveEntries -join ", ")."
    }

    if (Compare-Object `
            -ReferenceObject @($entryNames | Sort-Object) `
            -DifferenceObject @($actualEntries | Sort-Object)) {
        throw "The TestAdapter ZIP does not exactly match testadapter-package.txt."
    }

    $emptyEntries = @($archive.Entries | Where-Object Length -eq 0 | ForEach-Object FullName)
    if ($emptyEntries.Count -ne 0) {
        throw "The TestAdapter ZIP contains empty entries: $($emptyEntries -join ", ")."
    }
}
finally {
    $archive.Dispose()
}
