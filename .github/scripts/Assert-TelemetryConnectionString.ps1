#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [AllowNull()]
    [string] $ConnectionString = $env:RUSTANALYZER_TELEMETRY_CONNECTION_STRING
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "The telemetry connection string is not configured."
}

if ($ConnectionString.ToCharArray().Where({ [char]::IsControl($_) }).Count -ne 0) {
    throw "The telemetry connection string contains control characters."
}

$segments = $ConnectionString.Split(";")
if ($segments[-1] -eq "") {
    $segments = $segments[0..($segments.Count - 2)]
}

$values = [Collections.Generic.Dictionary[string, string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($segment in $segments) {
    $separator = $segment.IndexOf("=")
    if ($separator -le 0 -or $separator -eq $segment.Length - 1) {
        throw "The telemetry connection string contains a malformed segment."
    }

    $key = $segment.Substring(0, $separator).Trim()
    $value = $segment.Substring($separator + 1).Trim()
    if ($key.Length -eq 0 -or $value.Length -eq 0) {
        throw "The telemetry connection string contains a malformed segment."
    }

    if ($values.ContainsKey($key)) {
        throw "The telemetry connection string contains a duplicate key."
    }

    $values.Add($key, $value)
}

$instrumentationKey = [Guid]::Empty
if (-not $values.ContainsKey("InstrumentationKey") -or
    -not [Guid]::TryParse($values["InstrumentationKey"], [ref] $instrumentationKey)) {
    throw "The telemetry connection string requires a GUID InstrumentationKey."
}

function Assert-HttpsEndpoint {
    param (
        [Collections.Generic.Dictionary[string, string]] $Values,
        [string] $Name,
        [bool] $Required
    )

    if (-not $Values.ContainsKey($Name)) {
        if ($Required) {
            throw "The telemetry connection string requires an absolute HTTPS $Name."
        }

        return
    }

    $endpoint = $null
    if (-not [Uri]::TryCreate($Values[$Name], [UriKind]::Absolute, [ref] $endpoint) -or
        $endpoint.Scheme -ne [Uri]::UriSchemeHttps -or
        [string]::IsNullOrWhiteSpace($endpoint.Host)) {
        throw "The telemetry connection string requires an absolute HTTPS $Name."
    }
}

Assert-HttpsEndpoint -Values $values -Name "IngestionEndpoint" -Required $true
Assert-HttpsEndpoint -Values $values -Name "LiveEndpoint" -Required $false

Write-Host "Telemetry connection string validation passed."
