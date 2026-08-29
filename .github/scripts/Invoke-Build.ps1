#Requires -PSEdition Core
#Requires -Version 7.1

# The single Release build invocation shared by the Commands table and cdp.yml (Ruling S). The
# Release build is itself the C# style and analyzer enforcement, so there is no analyzer switch or
# second /t:Rebuild pass.
[CmdletBinding()]
param (
    [ValidateRange(1, 99)]
    [int] $VisualStudioMajorVersion = 17
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VisualStudio.psm1") -Force

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$solution = Join-Path $repoRoot "src\RustAnalyzer.sln"
$projectsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "_built\projects"))
$projectOutputRootWithSeparator = "$projectsRoot$([IO.Path]::DirectorySeparatorChar)"
$msbuild = Get-VisualStudioTool -Name MSBuild -MajorVersion $VisualStudioMajorVersion
Write-Host "Using MSBuild: $msbuild"
& $msbuild `
    $solution `
    /m `
    /nologo `
    /nr:false `
    /restore `
    /t:Build `
    /p:Configuration=Release `
    /p:DeployExtension=false `
    "/p:RaVsProjectOutputRoot=$projectOutputRootWithSeparator" `
    /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}
