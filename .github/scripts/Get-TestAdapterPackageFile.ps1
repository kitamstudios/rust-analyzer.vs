#Requires -PSEdition Core
#Requires -Version 7.1

<#
.SYNOPSIS
    Resolves the curated KS.RustAnalyzer.TestAdapter.zip file list against its project output.

.DESCRIPTION
    The single reader of src/RustAnalyzer.TestAdapter/testadapter-package.txt. Both the local test
    gate and the CI zip step call it against the canonical RustAnalyzer.TestAdapter project output.

    Paths are returned without an existence check: Compress-Archive fails hard on a listed name that
    was not built.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$listPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\src\RustAnalyzer.TestAdapter\testadapter-package.txt"))
if (-not (Test-Path -LiteralPath $listPath -PathType Leaf)) {
    throw "The TestAdapter package file list is missing: $listPath."
}

$names = @(Get-Content -LiteralPath $listPath |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })
if ($names.Count -eq 0) {
    throw "The TestAdapter package file list $listPath names no file."
}

$seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$duplicateNames = @($names | Where-Object { -not $seenNames.Add($_) })
if ($duplicateNames.Count -ne 0) {
    throw "The TestAdapter package file list contains duplicate destination names: $($duplicateNames -join ", ")."
}

$canonicalOutputDirectory = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\..\_built\projects\RustAnalyzer.TestAdapter"))
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$names | ForEach-Object {
    $name = $_
    if ($name -in @(".", "..") -or
        [IO.Path]::IsPathRooted($name) -or
        $name.IndexOfAny([char[]]@("\", "/", ":")) -ge 0 -or
        [IO.Path]::GetFileName($name) -ne $name) {
        throw "The TestAdapter package file list entry must be a direct filename: $name."
    }

    $path = [IO.Path]::GetFullPath((Join-Path $resolvedOutputDirectory $name))
    if (-not [string]::Equals(
            [IO.Path]::GetFileName($path),
            $name,
            [StringComparison]::Ordinal)) {
        throw "The TestAdapter package file list entry changes after path normalization: $name."
    }

    $parent = [IO.Path]::GetFullPath([IO.Path]::GetDirectoryName($path))
    if (-not $parent.Equals(
            $canonicalOutputDirectory,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The TestAdapter package input must be in the canonical owner directory: $path."
    }

    $path
}
