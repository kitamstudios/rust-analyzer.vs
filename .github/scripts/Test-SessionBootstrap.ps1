#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "RustNightly.psm1") -Force
$nightlyManifest = Enable-SessionRustNightly

Write-Host "Current-session bootstrap state is valid."
Write-Host "Rust nightly: $($nightlyManifest.Release) ($($nightlyManifest.CommitHash))"
