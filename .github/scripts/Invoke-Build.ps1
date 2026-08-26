#Requires -PSEdition Core
#Requires -Version 7.1

# The single Release build invocation, shared by the Commands table and cdp.yml (Ruling S). It is the
# whole build step and nothing else: the Release build is itself the C# style and analyzer enforcement,
# so there is deliberately no analyzer switch and no second /t:Rebuild pass to host a lint gate that
# Ruling N deleted.
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

# D4: every project's output is redirected to one _built directory. The trailing separator is required
# because MSBuild concatenates OutDir with the file name.
$outputDirectory = Join-Path $repoRoot "_built"
$outputDirectoryWithSeparator = "$outputDirectory$([IO.Path]::DirectorySeparatorChar)"

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
    "/p:OutDir=$outputDirectoryWithSeparator" `
    /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}
