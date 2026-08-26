Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot "SessionState.psm1")

function Get-AssistantBootstrapPath {
    return Join-Path (Get-RepositorySessionRoot) "assistant-bootstrap.json"
}

function New-AssistantBootstrapAuthorization {
    $authorizationPath = Get-AssistantBootstrapPath
    if (Test-Path -LiteralPath $authorizationPath) {
        throw "Assistant bootstrap provenance already exists for this session."
    }

    $tokenBytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($tokenBytes)
    $token = [Convert]::ToHexString($tokenBytes)
    $authorization = [ordered]@{
        SchemaVersion = 1
        SessionId = Get-RepositorySessionId
        RepositoryRoot = Get-RepositoryRoot
        Owner = "assistant"
        Phase = "authorized"
        TokenHash = Get-Sha256Hex -Value $token
        CreatedUtc = [DateTime]::UtcNow.ToString("O")
    }

    $sessionRoot = Get-RepositorySessionRoot
    New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
    [IO.File]::WriteAllText(
        $authorizationPath,
        ($authorization | ConvertTo-Json -Depth 3) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))

    return [pscustomobject]@{
        Token = $token
        TokenHash = $authorization.TokenHash
    }
}

function Get-AssistantBootstrapProvenance {
    param (
        [string[]] $AllowedPhases = @("ready")
    )

    $authorizationPath = Get-AssistantBootstrapPath
    if (-not (Test-Path -LiteralPath $authorizationPath -PathType Leaf)) {
        throw "Assistant bootstrap provenance is missing."
    }

    $provenance = Get-Content -LiteralPath $authorizationPath -Raw | ConvertFrom-Json
    if ($provenance.SchemaVersion -ne 1 -or
        $provenance.SessionId -ne (Get-RepositorySessionId) -or
        [IO.Path]::GetFullPath($provenance.RepositoryRoot) -ne (Get-RepositoryRoot) -or
        $provenance.Owner -ne "assistant" -or
        $AllowedPhases -notcontains $provenance.Phase -or
        $provenance.TokenHash -notmatch "^[0-9A-F]{64}$") {
        throw "Assistant bootstrap provenance is invalid for this session/phase; no self-healing or stale fallback is allowed."
    }

    return $provenance
}

function Assert-AssistantBootstrapAuthorization {
    param (
        [Parameter(Mandatory)]
        [string] $Token,

        [Parameter(Mandatory)]
        [string[]] $AllowedPhases
    )

    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw "Assistant bootstrap token is required. Dave and Bhaskar must hand back to JARVIS."
    }

    $provenance = Get-AssistantBootstrapProvenance -AllowedPhases $AllowedPhases
    if ((Get-Sha256Hex -Value $Token) -ne $provenance.TokenHash) {
        throw "Assistant bootstrap token is invalid. Dave and Bhaskar must hand back to JARVIS."
    }

    return $provenance
}

function Set-AssistantBootstrapPhase {
    param (
        [Parameter(Mandatory)]
        [string] $Token,

        [Parameter(Mandatory)]
        [ValidateSet("ready", "failed")]
        [string] $Phase
    )

    $provenance = Assert-AssistantBootstrapAuthorization `
        -Token $Token `
        -AllowedPhases @("authorized")
    $provenance.Phase = $Phase
    [IO.File]::WriteAllText(
        (Get-AssistantBootstrapPath),
        ($provenance | ConvertTo-Json -Depth 3) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

Export-ModuleMember -Function `
    Get-AssistantBootstrapPath, `
    New-AssistantBootstrapAuthorization, `
    Get-AssistantBootstrapProvenance, `
    Assert-AssistantBootstrapAuthorization, `
    Set-AssistantBootstrapPhase
