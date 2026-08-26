#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "RustNightly.psm1") -Force

$channel = Get-PinnedRustNightlyChannel
$manifestPath = Get-RustNightlyManifestPath
if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
}

# The bootstrap owns this directory; nothing else creates it.
$manifestDirectory = Split-Path -Parent $manifestPath
if (-not (Test-Path -LiteralPath $manifestDirectory -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $manifestDirectory -Force)
}

$rustupCommand = Get-Command rustup.exe -ErrorAction SilentlyContinue
if (-not $rustupCommand) {
    $rustupCommand = Get-Command rustup -ErrorAction SilentlyContinue
}

if (-not $rustupCommand) {
    throw "rustup was not found. Nightly bootstrap cannot continue."
}

Write-Host "Installing or updating Rust $channel for this checkout..."
& $rustupCommand.Source toolchain install $channel `
    --profile minimal `
    --component rustfmt `
    --component clippy `
    --target x86_64-pc-windows-msvc `
    --no-self-update
if ($LASTEXITCODE -ne 0) {
    throw "rustup failed to install/update the required nightly toolchain."
}

$nightly = & {
    $rustup = (Get-Command rustup.exe -ErrorAction SilentlyContinue)
    if (-not $rustup) {
        $rustup = Get-Command rustup
    }

    $output = @(& $rustup.Source run $channel rustc -Vv 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "The newly installed nightly rustc probe failed."
    }

    $values = @{}
    foreach ($line in $output) {
        if ($line -match "^(?<Name>[^:]+):\s*(?<Value>.*)$") {
            $values[$Matches.Name] = $Matches.Value
        }
    }

    [pscustomobject]@{
        Version = [string]$output[0]
        CommitHash = $values["commit-hash"]
        CommitDate = $values["commit-date"]
        Host = $values["host"]
        Release = $values["release"]
    }
}

if ([string]::IsNullOrWhiteSpace($nightly.CommitHash) -or
    [string]::IsNullOrWhiteSpace($nightly.Release) -or
    [string]::IsNullOrWhiteSpace($nightly.Host)) {
    throw "The newly installed nightly toolchain did not report complete version diagnostics."
}

$cargoVersion = (& $rustupCommand.Source run $channel cargo --version 2>&1) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) {
    throw "The newly installed nightly cargo probe failed."
}

$manifest = [ordered]@{
    SchemaVersion = 1
    RepositoryRoot = Get-RepositoryRoot
    Toolchain = $channel
    RustcVersion = $nightly.Version
    CommitHash = $nightly.CommitHash
    CommitDate = $nightly.CommitDate
    Release = $nightly.Release
    Host = $nightly.Host
    CargoVersion = $cargoVersion
    CreatedUtc = [DateTime]::UtcNow.ToString("O")
}
$json = $manifest | ConvertTo-Json -Depth 3
[IO.File]::WriteAllText(
    $manifestPath,
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Host "Rust nightly: $($nightly.Version)"
Write-Host "Rust nightly commit: $($nightly.CommitHash)"
Write-Host "Rust nightly manifest for this checkout: $manifestPath"