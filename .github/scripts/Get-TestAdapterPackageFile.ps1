#Requires -PSEdition Core
#Requires -Version 7.1

<#
.SYNOPSIS
    Resolves the curated KS.RustAnalyzer.TestAdapter.zip file list against a build output directory.

.DESCRIPTION
    The single reader of src/RustAnalyzer.TestAdapter/testadapter-package.txt. Both the local test gate
    and the CI zip step call this so the packaged file list has exactly one home.

    Paths are returned without an existence check: Compress-Archive must keep failing hard on a listed
    name that was not built.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$names | ForEach-Object { Join-Path $OutputDirectory $_ }
