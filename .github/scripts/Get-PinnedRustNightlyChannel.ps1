#Requires -PSEdition Core
#Requires -Version 7.1

<#
.SYNOPSIS
    Defines the single reader of the repository's pinned Rust nightly channel.

.DESCRIPTION
    Dot-source this file, then call Get-PinnedRustNightlyChannel. RustNightly.psm1 dot-sources it so the
    bootstrap path and the workflow share one regex and one error message; the workflow dot-sources it
    directly so reading a one-line file does not pull in the bootstrap module stack.
#>

function Get-PinnedRustNightlyChannel {
    $channelPath = Join-Path $PSScriptRoot "..\rust-nightly-channel"
    if (-not (Test-Path -LiteralPath $channelPath -PathType Leaf)) {
        throw "The pinned Rust nightly channel file is missing: $channelPath."
    }

    $channel = (Get-Content -LiteralPath $channelPath -Raw).Trim()
    if ($channel -notmatch "^nightly-\d{4}-\d{2}-\d{2}$") {
        throw "'$channel' in $channelPath is not a dated nightly channel."
    }

    return $channel
}
