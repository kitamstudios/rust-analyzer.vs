#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "T11Validation.psm1") -Force

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$artifactRoot = Join-Path $repositoryRoot "_built"
$manifestPath = Join-Path $artifactRoot "t11\canonical-artifacts.json"
$manifest = New-T11ArtifactManifest `
    -ArtifactRoot $artifactRoot `
    -ManifestPath $manifestPath

foreach ($artifact in $manifest.Artifacts) {
    Write-Host "$($artifact.Name): SHA-256 $($artifact.Sha256), $($artifact.ByteLength) bytes"
}
Write-Host "T11 artifact manifest: $manifestPath"
