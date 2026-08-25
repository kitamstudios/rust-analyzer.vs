Set-StrictMode -Version Latest

function Get-RepositorySessionId {
    $variableNames = @(
        "RUST_ANALYZER_VS_SESSION_ID",
        "AGENCY_SESSION_ID",
        "COPILOT_AGENT_SESSION_ID"
    )

    foreach ($variableName in $variableNames) {
        $value = [Environment]::GetEnvironmentVariable($variableName)
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        if ($value.Length -gt 128 -or $value -notmatch "^[A-Za-z0-9._-]+$") {
            throw "The session identifier from $variableName contains unsupported characters."
        }

        return $value
    }

    throw "No repository session identifier is available."
}

function Get-RepositorySessionRoot {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is required for session-scoped state."
    }

    $sessionId = Get-RepositorySessionId
    $sessionHash = Get-Sha256Hex -Value $sessionId
    return Join-Path $env:LOCALAPPDATA "ravsq\$($sessionHash.Substring(0, 16).ToLowerInvariant())"
}

function Get-RepositoryRoot {
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}

function Get-Sha256Hex {
    param (
        [Parameter(Mandatory)]
        [string] $Value
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString(
            $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))
    }
    finally {
        $sha256.Dispose()
    }
}

Export-ModuleMember -Function `
    Get-RepositorySessionId, `
    Get-RepositorySessionRoot, `
    Get-RepositoryRoot, `
    Get-Sha256Hex
