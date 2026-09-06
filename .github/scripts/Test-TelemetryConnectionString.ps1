#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$validator = Join-Path $PSScriptRoot "Assert-TelemetryConnectionString.ps1"
$instrumentationKey = "00000000-0000-0000-0000-000000000001"

function Assert-Failure {
    param (
        [scriptblock] $Action,
        [string] $Message,
        [string] $SensitiveValue
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") {
            throw "Expected failure containing '$Message'."
        }

        if ($SensitiveValue -and $_.Exception.Message.Contains($SensitiveValue)) {
            throw "Validation exposed the rejected connection string."
        }

        return
    }

    throw "Expected validation to fail."
}

& $validator -ConnectionString "ApplicationId=optional;LiveEndpoint=https://live.example.invalid/;InstrumentationKey=$instrumentationKey;IngestionEndpoint=https://ingest.example.invalid/;Authorization=ikey"
& $validator -ConnectionString "IngestionEndpoint=https://ingest.example.invalid;InstrumentationKey=$instrumentationKey"

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=$instrumentationKey"
} "IngestionEndpoint" $instrumentationKey

Assert-Failure {
    & $validator -ConnectionString "IngestionEndpoint=https://ingest.example.invalid"
} "GUID InstrumentationKey" "https://ingest.example.invalid"

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=$instrumentationKey;malformed;IngestionEndpoint=https://ingest.example.invalid"
} "malformed segment" "malformed;IngestionEndpoint"

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=$instrumentationKey;instrumentationkey=$instrumentationKey;IngestionEndpoint=https://ingest.example.invalid"
} "duplicate key" $instrumentationKey

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=not-a-guid;IngestionEndpoint=https://ingest.example.invalid"
} "GUID InstrumentationKey" "not-a-guid"

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=$instrumentationKey;IngestionEndpoint=http://ingest.example.invalid"
} "HTTPS IngestionEndpoint" "http://ingest.example.invalid"

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=$instrumentationKey;IngestionEndpoint=https://ingest.example.invalid;LiveEndpoint=http://live.example.invalid"
} "HTTPS LiveEndpoint" "http://live.example.invalid"

Assert-Failure {
    & $validator -ConnectionString "InstrumentationKey=$instrumentationKey;IngestionEndpoint=https://ingest.example.invalid`nControl=value"
} "control characters" "Control=value"

$originalConnectionString = $env:RUSTANALYZER_TELEMETRY_CONNECTION_STRING
try {
    Remove-Item Env:RUSTANALYZER_TELEMETRY_CONNECTION_STRING -ErrorAction SilentlyContinue
    Assert-Failure {
        & $validator
    } "not configured" $null
}
finally {
    $env:RUSTANALYZER_TELEMETRY_CONNECTION_STRING = $originalConnectionString
}

Write-Host "Telemetry connection string tests passed: 11."
