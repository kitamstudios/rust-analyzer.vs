#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet("unit", "integration", "acceptance", "full")]
    [string] $Mode,
    [ValidateRange(1, 99)]
    [int] $VisualStudioMajorVersion = 17
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$env:RUSTANALYZER_TELEMETRY_DISABLED = "1"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$buildRoot = Join-Path $repoRoot "_built"
$projectsDirectory = Join-Path $buildRoot "projects"
$testAdapterDirectory = Join-Path $projectsDirectory "RustAnalyzer.TestAdapter"
$testAdapterPackage = Join-Path $testAdapterDirectory "KS.RustAnalyzer.TestAdapter.zip"

$runsAssemblyTests = $Mode -ne "acceptance"
$runsAcceptanceHarness = $Mode -eq "acceptance" -or $Mode -eq "full"

Write-Host "Test phase: TestAdapter packager regression"
& (Join-Path $PSScriptRoot "Test-New-TestAdapterPackage.ps1")

# Nightly is required wherever Cargo, rustup, or the test adapter run as child processes. The manifest
# is validated and RUSTUP_TOOLCHAIN is exported in this process, so every child inherits it.
if ($Mode -ne "unit") {
    Import-Module (Join-Path $PSScriptRoot "RustNightly.psm1") -Force
    $nightlyManifest = Enable-PinnedRustNightly
    Write-Host "Using pinned Rust nightly: $($nightlyManifest.Release) ($($nightlyManifest.CommitHash))"
}

$assemblyTestExitCode = 0
$zeroTestFailure = $null
$taxonomyFailure = $null
$taxonomyTestClass = "KS.RustAnalyzer.UnitTests.TraitTaxonomyTests"
if ($runsAssemblyTests) {
    $runner = Join-Path $projectsDirectory "RustAnalyzer.UnitTests\xunit.console.exe"
    if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
        throw "The xUnit console runner was not found: $runner. Run the build command first."
    }

    $testProjects = @(
        "RustAnalyzer.Remote.UnitTests",
        "RustAnalyzer.TestAdapter.UnitTests",
        "RustAnalyzer.UnitTests")
    $assemblies = @(
        $testProjects |
            ForEach-Object { Join-Path $projectsDirectory "$_\KS.$_.dll" } |
            Sort-Object)
    $missingAssemblies = @($assemblies | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
    if ($missingAssemblies.Count -ne 0) {
        throw "Canonical test assemblies were not found: $($missingAssemblies -join ", "). Run the build command first."
    }
    $env:RAVS_XUNIT_TEST_ASSEMBLIES = $assemblies -join [IO.Path]::PathSeparator

    # full runs unfiltered, so a case carrying no type trait still runs; TraitTaxonomyTests is what fails it.
    # The array subexpression is required: a switch branch yielding @() would collapse to $null.
    $filterArguments = @(switch ($Mode) {
            "unit" { "-trait", "type=UnitTests" }
            "integration" { "-trait", "type=IntegrationTests" }
        })

    $testResultsDirectory = Join-Path $repoRoot "TestResults"
    New-Item -ItemType Directory -Path $testResultsDirectory -Force | Out-Null
    $testResultPath = Join-Path $testResultsDirectory "$Mode.xml"
    if (Test-Path -LiteralPath $testResultPath) {
        Remove-Item -LiteralPath $testResultPath -Force
    }

    # assemblies, not all: all also parallelizes collections within an assembly and overrides the
    # assembly's own CollectionBehavior, so a declared DisableTestParallelization would be discarded.
    $runnerArguments = @($assemblies) + $filterArguments + @("-parallel", "assemblies", "-xml", $testResultPath)

    $filterDescription = if ($filterArguments.Count -gt 0) { $filterArguments -join " " } else { "<none>" }
    Write-Host "Using xUnit console runner: $runner"
    Write-Host "Test filter: $filterDescription"
    if ($runsAcceptanceHarness) {
        Write-Host "The $Mode gate runs the standalone acceptance harness after the assembly tests."
    }

    & $runner @runnerArguments
    $assemblyTestExitCode = $LASTEXITCODE

    # A filter that matches nothing makes the runner report GRAND TOTAL 0 and exit 0, so the exit code
    # alone would report success having executed nothing. The count comes from the result XML rather
    # than the console text, and the assertion is only that it is greater than zero.
    $executedTestCount = 0
    $taxonomyTestCount = 0
    if (Test-Path -LiteralPath $testResultPath -PathType Leaf) {
        $testResults = [xml](Get-Content -LiteralPath $testResultPath -Raw)
        foreach ($assemblyResult in $testResults.SelectNodes("/assemblies/assembly")) {
            $executedTestCount += [int]$assemblyResult.GetAttribute("total")
        }

        $taxonomyTestCount = @($testResults.SelectNodes("//test[@type='$taxonomyTestClass']")).Count
    }

    Write-Host "Executed test count: $executedTestCount"
    if ($executedTestCount -eq 0) {
        $zeroTestFailure = "The $Mode gate executed no test. Filter: $filterDescription. Assemblies scanned: $($assemblies -join ", "). A filter that selects nothing exits 0, so the gate fails closed here."
    }

    # A non-zero total is not enough: the taxonomy invariants are what make every other case's
    # classification trustworthy, and losing the one assembly that carries them leaves a run that is
    # green and ungoverned. Only unit and full select type=UnitTests, so only they can assert this.
    if ($executedTestCount -gt 0 -and $Mode -in @("unit", "full") -and $taxonomyTestCount -eq 0) {
        $taxonomyFailure = "The $Mode gate executed $executedTestCount test(s) but none from $taxonomyTestClass, so the trait taxonomy went unenforced."
    }
}

# Runs even when the assembly leg already failed, so one gate run reports both failures.
$acceptanceFailure = $null
if ($runsAcceptanceHarness) {
    $acceptanceScript = Join-Path $repoRoot "src\TestProjects\run-integrationtests.ps1"
    try {
        & (Join-Path $PSScriptRoot "New-TestAdapterPackage.ps1") `
            -OutputDirectory $testAdapterDirectory `
            -DestinationPath $testAdapterPackage

        $expandedAdapterDirectory = Join-Path $testAdapterDirectory "testadapter"
        if (Test-Path -LiteralPath $expandedAdapterDirectory) {
            Remove-Item -LiteralPath $expandedAdapterDirectory -Recurse -Force
        }

        Expand-Archive -LiteralPath $testAdapterPackage -DestinationPath $expandedAdapterDirectory
        Write-Host "Acceptance test adapter: $expandedAdapterDirectory (derived from $testAdapterDirectory)"

        & $acceptanceScript `
            -SrcDir (Join-Path $repoRoot "src\TestProjects\workspace_with_tests") `
            -TestAdapterLocation $expandedAdapterDirectory `
            -VisualStudioMajorVersion $VisualStudioMajorVersion
    }
    catch {
        $acceptanceFailure = $_
    }
}

if ($assemblyTestExitCode -ne 0) {
    Write-Error "The xUnit console runner failed with exit code $assemblyTestExitCode." -ErrorAction Continue
}

if ($zeroTestFailure) {
    Write-Error $zeroTestFailure -ErrorAction Continue
}

if ($taxonomyFailure) {
    Write-Error $taxonomyFailure -ErrorAction Continue
}

if ($acceptanceFailure) {
    Write-Error "The standalone test-adapter acceptance leg failed: $acceptanceFailure" -ErrorAction Continue
}

if ($assemblyTestExitCode -ne 0 -or $zeroTestFailure -or $taxonomyFailure -or $acceptanceFailure) {
    throw "One or more test groups failed."
}
