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

$expectedDiscoveredTests = 204
$expectedUnitTests = 96
$expectedIntegrationTests = 108
$expectedExternalTests = 1
$unitFilter = "type=UnitTests"
$integrationFilter = "type=IntegrationTests"
$externalFilter = "scope=External"
$defaultFullFilter = "scope!=External"

function Get-DiscoveredTests {
    param ([string] $Filter)

    $listArguments = @($assemblies) + "/ListTests"
    if (-not [string]::IsNullOrEmpty($Filter)) {
        $listArguments += "/TestCaseFilter:$Filter"
    }

    $listOutput = @(& $vstest @listArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $listOutput | ForEach-Object { Write-Host $_ }
        throw "VSTest discovery failed for filter '$(if ($Filter) { $Filter } else { "<none>" })' with exit code $LASTEXITCODE."
    }

    @($listOutput |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -match "^KS\.RustAnalyzer\." })
}

$discoveredTests = @(Get-DiscoveredTests)
$unitTests = @(Get-DiscoveredTests -Filter $unitFilter)
$integrationTests = @(Get-DiscoveredTests -Filter $integrationFilter)
$externalTests = @(Get-DiscoveredTests -Filter $externalFilter)

$unitSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$integrationSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($test in $unitTests) {
    $null = $unitSet.Add($test)
}

foreach ($test in $integrationTests) {
    $null = $integrationSet.Add($test)
}

$missingTypeTests = @($discoveredTests |
    Where-Object { -not $unitSet.Contains($_) -and -not $integrationSet.Contains($_) })
if ($missingTypeTests.Count -ne 0) {
    throw "Test classification is missing type=UnitTests or type=IntegrationTests for: $(@($missingTypeTests | Select-Object -First 5) -join "; ")."
}

$bothTypeTests = @($unitTests | Where-Object { $integrationSet.Contains($_) })
if ($bothTypeTests.Count -ne 0) {
    throw "Tests cannot carry both type=UnitTests and type=IntegrationTests: $(@($bothTypeTests | Select-Object -First 5) -join "; ")."
}

$externalOutsideIntegration = @($externalTests | Where-Object { -not $integrationSet.Contains($_) })
if ($externalOutsideIntegration.Count -ne 0) {
    throw "Every scope=External test must also be type=IntegrationTests: $(@($externalOutsideIntegration | Select-Object -First 5) -join "; ")."
}

if ($discoveredTests.Count -ne $expectedDiscoveredTests -or
    $unitTests.Count -ne $expectedUnitTests -or
    $integrationTests.Count -ne $expectedIntegrationTests -or
    $externalTests.Count -ne $expectedExternalTests) {
    throw "Test-classification drift: total=$($discoveredTests.Count) (expected $expectedDiscoveredTests), unit=$($unitTests.Count) (expected $expectedUnitTests), integration=$($integrationTests.Count) (expected $expectedIntegrationTests), external subset=$($externalTests.Count) (expected $expectedExternalTests). Explicitly classify every added or renamed xUnit test."
}

$filter = if ($Full) {
    if ($IncludeExternal) { $null } else { $defaultFullFilter }
}
else {
    $unitFilter
}

$selectedTests = @(
    if (-not $Full) {
        $unitTests
    }
    elseif ($IncludeExternal) {
        $discoveredTests
    }
    else {
        Get-DiscoveredTests -Filter $defaultFullFilter
    }
)
$selectedTestCount = if ($Full -and $IncludeExternal) {
    $expectedDiscoveredTests
}
elseif ($Full) {
    $expectedDiscoveredTests - $expectedExternalTests
}
else {
    $expectedUnitTests
}
if ($selectedTests.Count -ne $selectedTestCount) {
    throw "VSTest gate-filter drift for '$($filter ?? "<none>")': selected $($selectedTests.Count) tests; expected $selectedTestCount."
}

Write-Host "Classification: unit=$($unitTests.Count), integration=$($integrationTests.Count) (external subset=$($externalTests.Count)), assembly total=$($discoveredTests.Count)."
Write-Host "Gate filter '$($filter ?? "<none>")' selected $($selectedTests.Count) assembly test(s)."
if ($Full) {
    Write-Host "The full gate runs the standalone acceptance harness after the assembly tests."
}

if ($ValidateClassificationOnly) {
    return
}

$testResultsDirectory = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Path $testResultsDirectory -Force | Out-Null
$testResultName = if ($Full) { "full.trx" } else { "quick.trx" }
$testResultPath = Join-Path $testResultsDirectory $testResultName
if (Test-Path -LiteralPath $testResultPath) {
    Remove-Item -LiteralPath $testResultPath -Force
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
    "/Logger:trx;LogFileName=$testResultPath"
)
if ($filter) {
    $vstestArguments += "/TestCaseFilter:$filter"
}

$env:RUSTANALYZER_TELEMETRY_DISABLED = "1"
Write-Host "Using VSTest: $vstest"
Write-Host "Test filter: $(if ($filter) { $filter } else { "<none>" })"
$assemblyTestExitCode = Invoke-VSTestProcess -VSTestPath $vstest -Arguments $vstestArguments

$acceptanceFailure = $null
if ($Full) {
    $acceptanceScript = Join-Path $repoRoot "src\TestProjects\run-integrationtests.ps1"
    try {
        & $acceptanceScript `
            -SrcDir (Join-Path $repoRoot "src\TestProjects\workspace_with_tests") `
            -TestAdapterLocation $outputDirectory `
            -VSTestPath $vstest `
            -VisualStudioMajorVersion $VisualStudioMajorVersion
    }
    catch {
        $acceptanceFailure = $_
    }
}

if ($assemblyTestExitCode -ne 0) {
    Write-Error "VSTest failed with exit code $assemblyTestExitCode." -ErrorAction Continue
}

if ($acceptanceFailure) {
    Write-Error "The standalone test-adapter acceptance harness failed: $acceptanceFailure" -ErrorAction Continue
}

if ($assemblyTestExitCode -ne 0 -or $acceptanceFailure) {
    throw "One or more test groups failed."
}
