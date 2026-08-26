#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [switch] $NoRestore,
    [ValidateRange(1, 99)]
    [int] $VisualStudioMajorVersion = 17
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VisualStudio.psm1") -Force

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$solution = Join-Path $repoRoot "src\RustAnalyzer.sln"
$outputDirectory = Join-Path $repoRoot "_built"
$outputDirectoryWithSeparator = "$outputDirectory$([IO.Path]::DirectorySeparatorChar)"
$msbuild = Get-VisualStudioTool -Name MSBuild -MajorVersion $VisualStudioMajorVersion

$msbuildArguments = @(
    $solution,
    "/m",
    "/nologo",
    "/nr:false",
    "/t:Build",
    "/p:Configuration=Release",
    "/p:DeployExtension=false",
    "/p:OutDir=$outputDirectoryWithSeparator",
    "/verbosity:minimal"
)

if (-not $NoRestore) {
    $msbuildArguments += "/restore"
}

Write-Host "Using MSBuild: $msbuild"
& $msbuild @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}
