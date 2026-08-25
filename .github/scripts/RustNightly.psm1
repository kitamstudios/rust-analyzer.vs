Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot "AssistantBootstrap.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "CIProvenance.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "SessionState.psm1") -Force

function Get-RustNightlyHandoffMessage {
    param (
        [Parameter(Mandatory)]
        [string] $Message
    )

    if ($env:GITHUB_ACTIONS -eq "true") {
        return "$Message The trusted workflow must run Initialize-CISession.ps1 before test gates."
    }

    return "$Message Hand back to JARVIS to run the assistant-only session startup bootstrap."
}

function Get-RustupPath {
    $command = Get-Command rustup.exe -ErrorAction SilentlyContinue
    if (-not $command) {
        $command = Get-Command rustup -ErrorAction SilentlyContinue
    }

    if (-not $command) {
        throw (Get-RustNightlyHandoffMessage "rustup was not found.")
    }

    return $command.Source
}

function Get-RustcNightlyInfo {
    $rustup = Get-RustupPath
    $output = @(& $rustup run nightly rustc -Vv 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw (Get-RustNightlyHandoffMessage "The installed nightly rustc probe failed.")
    }

    $values = @{}
    foreach ($line in $output) {
        if ($line -match "^(?<Name>[^:]+):\s*(?<Value>.*)$") {
            $values[$Matches.Name] = $Matches.Value
        }
    }

    $version = [string]$output[0]
    $requiredKeys = @("commit-hash", "commit-date", "host", "release")
    foreach ($requiredKey in $requiredKeys) {
        if ([string]::IsNullOrWhiteSpace($values[$requiredKey])) {
            throw (Get-RustNightlyHandoffMessage "The nightly rustc probe did not report '$requiredKey'.")
        }
    }

    return [pscustomobject]@{
        Version = $version
        CommitHash = $values["commit-hash"]
        CommitDate = $values["commit-date"]
        Host = $values["host"]
        Release = $values["release"]
    }
}

function Get-RustNightlyManifest {
    try {
        $provenance = Get-AssistantBootstrapProvenance -AllowedPhases @("ready")
    }
    catch {
        try {
            $provenance = Get-CIBootstrapProvenance
        }
        catch {
            throw (Get-RustNightlyHandoffMessage "Neither assistant nor trusted GitHub Actions provenance is valid. $($_.Exception.Message)")
        }
    }

    $sessionId = Get-RepositorySessionId
    $sessionRoot = Get-RepositorySessionRoot
    $manifestPath = Join-Path $sessionRoot "rust-nightly.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw (Get-RustNightlyHandoffMessage "The current session has no Rust nightly manifest; stable fallback is forbidden.")
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 1 -or
        $manifest.SessionId -ne $sessionId -or
        $manifest.Toolchain -ne "nightly") {
        throw (Get-RustNightlyHandoffMessage "The Rust nightly manifest does not belong to the current session.")
    }

    $repoRoot = Get-RepositoryRoot
    if ([IO.Path]::GetFullPath($manifest.RepositoryRoot) -ne $repoRoot) {
        throw (Get-RustNightlyHandoffMessage "The Rust nightly manifest belongs to a different repository checkout.")
    }

    if ($manifest.BootstrapOwner -ne $provenance.Owner -or
        $manifest.BootstrapPhase -ne "ready" -or
        $manifest.BootstrapTokenHash -ne $provenance.TokenHash) {
        throw (Get-RustNightlyHandoffMessage "The Rust nightly manifest lacks matching assistant bootstrap provenance.")
    }

    return $manifest
}

function Enable-SessionRustNightly {
    $manifest = Get-RustNightlyManifest
    $nightly = Get-RustcNightlyInfo
    if ($nightly.CommitHash -ne $manifest.CommitHash -or
        $nightly.Release -ne $manifest.Release -or
        $nightly.Host -ne $manifest.Host) {
        throw (Get-RustNightlyHandoffMessage "The installed nightly toolchain no longer matches the current-session manifest.")
    }

    $env:RUSTUP_TOOLCHAIN = "nightly"
    $cargoVersion = (& cargo --version 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $cargoVersion -ne $manifest.CargoVersion) {
        throw (Get-RustNightlyHandoffMessage "The nightly cargo proxy does not match the current-session manifest.")
    }

    return $manifest
}

Export-ModuleMember -Function Get-RustNightlyManifest, Enable-SessionRustNightly
