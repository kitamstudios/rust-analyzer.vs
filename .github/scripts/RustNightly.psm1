Set-StrictMode -Version Latest

# One reader, one regex, one message. The workflow dot-sources the same file rather than importing this
# module merely to read a one-line file.
. (Join-Path $PSScriptRoot "Get-PinnedRustNightlyChannel.ps1")

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

function Get-RustNightlyManifestPath {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is required to locate the Rust nightly manifest."
    }

    # Scoped to the checkout, not to a session: the pin is a dated nightly, which rustup treats as
    # immutable, so one install serves every session sharing this working tree. It is stored outside the
    # tree so that no build clean can reach it. Lower-cased before hashing because Windows paths are
    # case-insensitive and $PSScriptRoot carries whatever case the caller's location happened to have.
    $hash = Get-Sha256Hex -Value (Get-RepositoryRoot).ToLowerInvariant()
    return Join-Path $env:LOCALAPPDATA "ravsq\$($hash.Substring(0, 16).ToLowerInvariant())\rust-nightly.json"
}

function Get-RustNightlyHandoffMessage {
    param (
        [Parameter(Mandatory)]
        [string] $Message
    )

    return "$Message Hand back to JARVIS to run the assistant-only Rust nightly bootstrap for this checkout."
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
    $output = @(& $rustup run (Get-PinnedRustNightlyChannel) rustc -Vv 2>&1)
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
    $manifestPath = Get-RustNightlyManifestPath
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw (Get-RustNightlyHandoffMessage "This checkout has no Rust nightly manifest, so the nightly bootstrap has not run here; stable fallback is forbidden.")
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 1) {
        throw (Get-RustNightlyHandoffMessage "The Rust nightly manifest for this checkout is not a supported schema version.")
    }

    # A pin moved after the bootstrap leaves a manifest that is otherwise valid but records the previous
    # channel. The remedy is the same re-bootstrap; the diagnosis must not blame the toolchain.
    $channel = Get-PinnedRustNightlyChannel
    if ($manifest.Toolchain -ne $channel) {
        throw (Get-RustNightlyHandoffMessage "The Rust nightly manifest records channel '$($manifest.Toolchain)' but the repository is now pinned to '$channel'.")
    }

    # The manifest path already derives from the checkout, so this catches a hand-copied or hand-edited
    # manifest rather than an ordinary mismatch -- keep the diagnosis distinct from a stale pin.
    $repoRoot = Get-RepositoryRoot
    if ([IO.Path]::GetFullPath($manifest.RepositoryRoot) -ne $repoRoot) {
        throw (Get-RustNightlyHandoffMessage "The Rust nightly manifest records a different repository checkout, so it was not written by this checkout's bootstrap.")
    }

    return $manifest
}

function Enable-PinnedRustNightly {
    $manifest = Get-RustNightlyManifest
    $nightly = Get-RustcNightlyInfo
    if ($nightly.CommitHash -ne $manifest.CommitHash -or
        $nightly.Release -ne $manifest.Release -or
        $nightly.Host -ne $manifest.Host) {
        throw (Get-RustNightlyHandoffMessage "The installed nightly toolchain no longer matches this checkout's manifest.")
    }

    $env:RUSTUP_TOOLCHAIN = Get-PinnedRustNightlyChannel
    $cargoVersion = (& cargo --version 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $cargoVersion -ne $manifest.CargoVersion) {
        throw (Get-RustNightlyHandoffMessage "The nightly cargo proxy does not match this checkout's manifest.")
    }

    return $manifest
}

Export-ModuleMember -Function `
    Get-PinnedRustNightlyChannel, `
    Get-RepositoryRoot, `
    Get-RustNightlyManifestPath, `
    Get-RustNightlyManifest, `
    Enable-PinnedRustNightly
