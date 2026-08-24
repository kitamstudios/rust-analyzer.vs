#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [switch] $AssistantStartup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $AssistantStartup) {
    throw "Initialize-AssistantSession.ps1 is JARVIS-only. Dave and Bhaskar must hand back to JARVIS for session startup."
}

Import-Module (Join-Path $PSScriptRoot "AssistantBootstrap.psm1") -Force

$authorizationPath = Get-AssistantBootstrapPath
if (Test-Path -LiteralPath $authorizationPath -PathType Leaf) {
    try {
        [void](Get-AssistantBootstrapProvenance -AllowedPhases @("ready"))
        & (Join-Path $PSScriptRoot "Test-SessionBootstrap.ps1")
        Write-Host "Assistant-owned bootstrap is already valid for this session; no network, build, install, or update work was performed."
        return
    }
    catch {
        throw "Existing assistant bootstrap provenance/state is invalid. Start a new assistant session; in-session self-healing is forbidden. $($_.Exception.Message)"
    }
}

$authorization = New-AssistantBootstrapAuthorization
try {
    & (Join-Path $PSScriptRoot "Initialize-RustNightly.ps1") `
        -BootstrapToken $authorization.Token
    Set-AssistantBootstrapPhase `
        -Token $authorization.Token `
        -Phase "ready"

    & (Join-Path $PSScriptRoot "Test-SessionBootstrap.ps1")
}
catch {
    try {
        Set-AssistantBootstrapPhase `
            -Token $authorization.Token `
            -Phase "failed"
    }
    catch {
        Write-Host "Unable to mark assistant bootstrap provenance as failed: $($_.Exception.Message)"
    }

    throw
}
