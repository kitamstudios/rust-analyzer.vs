Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot "SessionState.psm1") -Force

function Get-CIBootstrapPath {
    return Join-Path (Get-RepositorySessionRoot) "ci-bootstrap.json"
}

function Get-CIIdentity {
    if ($env:GITHUB_ACTIONS -ne "true") {
        throw "CI bootstrap is allowed only in GitHub Actions."
    }

    $requiredVariables = @(
        "GITHUB_RUN_ID",
        "GITHUB_RUN_ATTEMPT",
        "GITHUB_WORKFLOW",
        "GITHUB_JOB",
        "GITHUB_REPOSITORY",
        "GITHUB_SHA"
    )
    foreach ($variable in $requiredVariables) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
            throw "GitHub Actions identity is missing $variable."
        }
    }

    return [ordered]@{
        RunId = $env:GITHUB_RUN_ID
        RunAttempt = $env:GITHUB_RUN_ATTEMPT
        Workflow = $env:GITHUB_WORKFLOW
        Job = $env:GITHUB_JOB
        Repository = $env:GITHUB_REPOSITORY
        Sha = $env:GITHUB_SHA
    }
}

function Get-CITokenHash {
    param (
        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Identity
    )

    $value = @(
        Get-RepositorySessionId
        Get-RepositoryRoot
        $Identity.RunId
        $Identity.RunAttempt
        $Identity.Workflow
        $Identity.Job
        $Identity.Repository
        $Identity.Sha
    ) -join "|"
    return Get-Sha256Hex -Value $value
}

function New-CIBootstrapProvenance {
    $identity = Get-CIIdentity
    $provenance = [ordered]@{
        SchemaVersion = 1
        SessionId = Get-RepositorySessionId
        RepositoryRoot = Get-RepositoryRoot
        Owner = "ci"
        Phase = "ready"
        TokenHash = Get-CITokenHash -Identity $identity
        RunId = $identity.RunId
        RunAttempt = $identity.RunAttempt
        Workflow = $identity.Workflow
        Job = $identity.Job
        Repository = $identity.Repository
        Sha = $identity.Sha
        CreatedUtc = [DateTime]::UtcNow.ToString("O")
    }

    $sessionRoot = Get-RepositorySessionRoot
    New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
    [IO.File]::WriteAllText(
        (Get-CIBootstrapPath),
        ($provenance | ConvertTo-Json -Depth 3) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))

    return $provenance
}

function Get-CIBootstrapProvenance {
    $identity = Get-CIIdentity
    $path = Get-CIBootstrapPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "CI bootstrap provenance is missing."
    }

    $provenance = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($provenance.SchemaVersion -ne 1 -or
        $provenance.SessionId -ne (Get-RepositorySessionId) -or
        [IO.Path]::GetFullPath($provenance.RepositoryRoot) -ne (Get-RepositoryRoot) -or
        $provenance.Owner -ne "ci" -or
        $provenance.Phase -ne "ready" -or
        $provenance.RunId -ne $identity.RunId -or
        $provenance.RunAttempt -ne $identity.RunAttempt -or
        $provenance.Workflow -ne $identity.Workflow -or
        $provenance.Job -ne $identity.Job -or
        $provenance.Repository -ne $identity.Repository -or
        $provenance.Sha -ne $identity.Sha -or
        $provenance.TokenHash -ne (Get-CITokenHash -Identity $identity)) {
        throw "CI bootstrap provenance does not match the current GitHub Actions run."
    }

    return $provenance
}

Export-ModuleMember -Function `
    Get-CIBootstrapPath, `
    New-CIBootstrapProvenance, `
    Get-CIBootstrapProvenance
