#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "CIProvenance.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "SessionState.psm1") -Force

$provenance = New-CIBootstrapProvenance
$rustup = Get-Command rustup.exe -ErrorAction SilentlyContinue
if (-not $rustup) {
    $rustup = Get-Command rustup -ErrorAction SilentlyContinue
}

if (-not $rustup) {
    throw "rustup was not found after the workflow's nightly setup step."
}

$rustcOutput = @(& $rustup.Source run nightly rustc -Vv 2>&1)
if ($LASTEXITCODE -ne 0) {
    $rustcOutput | ForEach-Object { Write-Host $_ }
    throw "The workflow-installed nightly rustc probe failed."
}

$rustcValues = @{}
foreach ($line in $rustcOutput) {
    if ($line -match "^(?<Name>[^:]+):\s*(?<Value>.*)$") {
        $rustcValues[$Matches.Name] = $Matches.Value
    }
}

$cargoVersion = (& $rustup.Source run nightly cargo --version 2>&1) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0 -or
    [string]::IsNullOrWhiteSpace($rustcValues["commit-hash"]) -or
    [string]::IsNullOrWhiteSpace($rustcValues["release"]) -or
    [string]::IsNullOrWhiteSpace($rustcValues["host"])) {
    throw "The workflow-installed nightly toolchain did not report complete diagnostics."
}

$manifest = [ordered]@{
    SchemaVersion = 1
    SessionId = Get-RepositorySessionId
    RepositoryRoot = Get-RepositoryRoot
    BootstrapOwner = $provenance.Owner
    BootstrapPhase = $provenance.Phase
    BootstrapTokenHash = $provenance.TokenHash
    Toolchain = "nightly"
    RustcVersion = [string]$rustcOutput[0]
    CommitHash = $rustcValues["commit-hash"]
    CommitDate = $rustcValues["commit-date"]
    Release = $rustcValues["release"]
    Host = $rustcValues["host"]
    CargoVersion = $cargoVersion
    CreatedUtc = [DateTime]::UtcNow.ToString("O")
}
[IO.File]::WriteAllText(
    (Join-Path (Get-RepositorySessionRoot) "rust-nightly.json"),
    ($manifest | ConvertTo-Json -Depth 3) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Host "CI nightly: $($manifest.RustcVersion)"
Write-Host "CI nightly commit: $($manifest.CommitHash)"
