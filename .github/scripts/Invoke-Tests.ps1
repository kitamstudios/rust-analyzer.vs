#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [switch] $Full,
    [switch] $IncludeExternal,
    [switch] $ValidateClassificationOnly,
    [ValidateRange(1, 99)]
    [int] $VisualStudioMajorVersion = 17
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($IncludeExternal -and -not $Full) {
    throw "-IncludeExternal is only valid with -Full."
}

Import-Module (Join-Path $PSScriptRoot "VisualStudio.psm1") -Force

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$outputDirectory = Join-Path $repoRoot "_built"
$vstest = Get-VisualStudioTool -Name VSTest -MajorVersion $VisualStudioMajorVersion
$assemblies = @(
    "KS.RustAnalyzer.UnitTests.dll",
    "KS.RustAnalyzer.TestAdapter.UnitTests.dll",
    "KS.RustAnalyzer.Remote.UnitTests.dll"
) | ForEach-Object {
    $path = Join-Path $outputDirectory $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Built test assembly not found: $path. Run the build command first."
    }

    $path
}

$classificationPath = Join-Path $repoRoot ".github\test-classification.json"
if (-not (Test-Path -LiteralPath $classificationPath -PathType Leaf)) {
    throw "Transitional test classification is missing: $classificationPath."
}

$classification = Get-Content -LiteralPath $classificationPath -Raw | ConvertFrom-Json
if ($classification.SchemaVersion -ne 1 -or $classification.Policy -ne "transitional-fqn") {
    throw "Unsupported transitional test-classification schema or policy."
}

$listOutput = @(& $vstest @assemblies /ListTests 2>&1)
if ($LASTEXITCODE -ne 0) {
    $listOutput | ForEach-Object { Write-Host $_ }
    throw "VSTest test discovery failed with exit code $LASTEXITCODE."
}

$discoveredTests = @($listOutput |
    ForEach-Object { ([string]$_).Trim() } |
    Where-Object { $_ -match "^KS\.RustAnalyzer\." })
if ($discoveredTests.Count -ne $classification.ExpectedDiscoveredTests) {
    throw "Test-classification drift: discovered $($discoveredTests.Count) tests; expected $($classification.ExpectedDiscoveredTests). Review .github/test-classification.json."
}

$integrationRules = @($classification.Integration)
$externalRules = @($classification.External)
foreach ($rule in @($integrationRules + $externalRules)) {
    if ([string]::IsNullOrWhiteSpace($rule.Prefix) -or $rule.ExpectedMatches -lt 1) {
        throw "Test-classification contains an invalid prefix/count rule."
    }

    $matches = @($discoveredTests |
        Where-Object { $_.StartsWith($rule.Prefix, [StringComparison]::Ordinal) })
    if ($matches.Count -ne $rule.ExpectedMatches) {
        throw "Test-classification drift for '$($rule.Prefix)': matched $($matches.Count); expected $($rule.ExpectedMatches)."
    }
}

$integrationTests = [Collections.Generic.List[string]]::new()
$externalTests = [Collections.Generic.List[string]]::new()
foreach ($test in $discoveredTests) {
    $integrationMatches = @($integrationRules |
        Where-Object { $test.StartsWith($_.Prefix, [StringComparison]::Ordinal) })
    $externalMatches = @($externalRules |
        Where-Object { $test.StartsWith($_.Prefix, [StringComparison]::Ordinal) })
    if ($integrationMatches.Count + $externalMatches.Count -gt 1) {
        throw "Test-classification overlap for '$test'."
    }

    if ($integrationMatches.Count -eq 1) {
        $integrationTests.Add($test)
    }
    elseif ($externalMatches.Count -eq 1) {
        $externalTests.Add($test)
    }
}

$unitCount = $discoveredTests.Count - $integrationTests.Count - $externalTests.Count
if ($unitCount -ne $classification.ExpectedUnitTests -or
    $integrationTests.Count -ne $classification.ExpectedIntegrationTests -or
    $externalTests.Count -ne $classification.ExpectedExternalTests) {
    throw "Test-classification totals drifted: unit=$unitCount, integration=$($integrationTests.Count), external=$($externalTests.Count)."
}

$integrationFilter = @($integrationRules | ForEach-Object { "FullyQualifiedName!~$($_.Prefix)" })
$externalFilter = @($externalRules | ForEach-Object { "FullyQualifiedName!~$($_.Prefix)" })
$filter = if ($Full) {
    if ($IncludeExternal) { $null } else { $externalFilter -join "&" }
}
else {
    @($integrationFilter + $externalFilter) -join "&"
}

$selectedTestCount = if ($Full) {
    if ($IncludeExternal) { $discoveredTests.Count } else { $discoveredTests.Count - $externalTests.Count }
}
else {
    $unitCount
}
Write-Host "Classification: unit=$unitCount, integration=$($integrationTests.Count), external=$($externalTests.Count)."
Write-Host "Selected by gate: $selectedTestCount test(s)."
if ($ValidateClassificationOnly) {
    $filteredListArguments = @($assemblies) + "/ListTests"
    if ($filter) {
        $filteredListArguments += "/TestCaseFilter:$filter"
    }

    $filteredListOutput = @(& $vstest @filteredListArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $filteredListOutput | ForEach-Object { Write-Host $_ }
        throw "Filtered VSTest discovery failed with exit code $LASTEXITCODE."
    }

    $filteredTests = @($filteredListOutput |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -match "^KS\.RustAnalyzer\." })
    if ($filteredTests.Count -ne $selectedTestCount) {
        throw "VSTest filter drift: selected $($filteredTests.Count) tests; expected $selectedTestCount."
    }

    Write-Host "Filtered VSTest discovery matched $($filteredTests.Count) test(s)."
    return
}

$nightlyManifest = $null
if ($Full) {
    Import-Module (Join-Path $PSScriptRoot "RustNightly.psm1") -Force
    $nightlyManifest = Enable-SessionRustNightly
    Write-Host "Using session Rust nightly: $($nightlyManifest.Release) ($($nightlyManifest.CommitHash))"
}

$vstestArguments = @(
    $assemblies
    "/Parallel"
    "/Logger:console;verbosity=normal"
)
if ($filter) {
    $vstestArguments += "/TestCaseFilter:$filter"
}

$env:RUSTANALYZER_TELEMETRY_DISABLED = "1"
Write-Host "Using VSTest: $vstest"
Write-Host "Test filter: $(if ($filter) { $filter } else { "<none>" })"
$assemblyTestExitCode = Invoke-VSTestProcess -VSTestPath $vstest -Arguments $vstestArguments

$integrationFailure = $null
if ($Full) {
    $integrationScript = Join-Path $repoRoot "src\TestProjects\run-integrationtests.ps1"
    try {
        & $integrationScript `
            -SrcDir (Join-Path $repoRoot "src\TestProjects\workspace_with_tests") `
            -TestAdapterLocation $outputDirectory `
            -VSTestPath $vstest `
            -VisualStudioMajorVersion $VisualStudioMajorVersion
    }
    catch {
        $integrationFailure = $_
    }
}

if ($assemblyTestExitCode -ne 0) {
    Write-Error "VSTest failed with exit code $assemblyTestExitCode." -ErrorAction Continue
}

if ($integrationFailure) {
    Write-Error "The standalone test-adapter integration harness failed: $integrationFailure" -ErrorAction Continue
}

if ($assemblyTestExitCode -ne 0 -or $integrationFailure) {
    throw "One or more test groups failed."
}
