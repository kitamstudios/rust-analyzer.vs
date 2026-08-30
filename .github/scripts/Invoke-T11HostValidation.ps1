#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet(17, 18)]
    [int] $VisualStudioMajorVersion,

    [Parameter(Mandatory)]
    [string] $ArtifactRoot,

    [Parameter(Mandatory)]
    [string] $DiagnosticsDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern("^[A-Za-z][A-Za-z0-9]{5,63}$")]
    [string] $RootSuffix,

    [ValidateRange(30, 1800)]
    [int] $ProcessTimeoutSeconds = 300,

    [ValidateRange(30, 1800)]
    [int] $AcceptanceTimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "T11Validation.psm1") -Force

function Write-Json {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Value
    )

    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Require-DiagnosticEvidence {
    param (
        [Parameter(Mandatory)]
        [string[]] $Names
    )

    foreach ($name in $Names) {
        [void]$requiredDiagnosticNames.Add($name)
    }
}

function Set-PhaseStatus {
    param (
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [ValidateSet("NotRun", "Running", "Passed", "Failed")]
        [string] $Status,

        [string] $Error
    )

    $phaseStatus[$Name] = [ordered]@{
        Status = $Status
        Error = $Error
    }
    Write-Json `
        -Path (Join-Path $diagnosticsDirectory "diagnostic-status.json") `
        -Value $phaseStatus
}

function Write-EvidenceMatrix {
    $hostName = "Visual Studio $VisualStudioMajorVersion"
    $isolationEvidence = if ($phaseStatus.Isolation.Error) {
        $phaseStatus.Isolation.Error
    }
    else {
        "Profile and generated paths are unique to this run"
    }
    $lines = @(
        "# T11 evidence matrix - $hostName",
        "",
        "| Claim | Evidence / scope | Status |",
        "| --- | --- | --- |",
        "| Main VSIX exact producer bytes | SHA-256 and byte length before host use | $($phaseStatus.ArtifactIntegrity.Status) |",
        "| Host selection and tools | Exactly one complete Core Editor host; all tools under that installation | $($phaseStatus.HostSelection.Status) |",
        "| Isolation ownership | $isolationEvidence | $($phaseStatus.Isolation.Status) |",
        "| Main VSIX installability | Selected-host VSIXInstaller in isolated root suffix | $($phaseStatus.Install.Status) |",
        "| Installed identity and version | Installed extension.vsixmanifest in isolated profile | $($phaseStatus.InstalledIdentity.Status) |",
        "| Shell startup | Exact selected devenv.exe, `/Log`, and built-in `File.Exit` | $($phaseStatus.Startup.Status) |",
        "| Scoped Activity Log | Main-extension registration/composition/binding/package-load errors | $($phaseStatus.ActivityLog.Status) |",
        "| TestAdapter exact producer bytes and acceptance | Exact archive, selected-host VSTest, explicit major $VisualStudioMajorVersion | $($phaseStatus.Acceptance.Status) |",
        "| Isolated-run cleanup | Confirmed Job Object zero, reserved profile, installer temp, and generated paths | $($phaseStatus.Cleanup.Status) |",
        "| Prerequisite, suspension, UI, logging, Rust-child suppression, accessibility, and process reset | Deterministic compiled tests; not claimed from real-host validation | Not a real-host claim |",
        "| Exact 17.12 execution | Predicate and manifest tests only | Not claimed |",
        "| Development Pack | Not in T11 - T13 | Not in T11 - T13 |",
        "| Pack publication | Not in T11 - T14 | Not in T11 - T14 |",
        "| Main publication dependency | Both host jobs are required by the workflow | Workflow contract |"
    )
    [IO.File]::WriteAllLines(
        (Join-Path $diagnosticsDirectory "evidence-matrix.md"),
        $lines,
        [Text.UTF8Encoding]::new($false))
}

function Assert-ProcessSucceeded {
    param (
        [Parameter(Mandatory)]
        [object] $Result,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ($Result.TimedOut) {
        throw "$Description timed out."
    }
    if (-not $Result.AssignedBeforeResume) {
        throw "$Description was not assigned to its Windows Job Object before resume."
    }
    if (-not $Result.JobZeroConfirmed -or
        -not $Result.ProcessTreeQuiescent -or
        $Result.CleanupFailed) {
        throw "$Description did not confirm Windows Job Object active-process zero."
    }
    if ($Result.ExitCode -ne 0) {
        throw "$Description exited with code $($Result.ExitCode)."
    }
}

function Write-PendingProcessEvidence {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [Parameter(Mandatory)]
        [int] $TimeoutSeconds
    )

    Write-Json `
        -Path $Path `
        -Value ([ordered]@{
            FilePath = $FilePath
            Arguments = $Arguments
            TimeoutSeconds = $TimeoutSeconds
            Status = "Process did not return a result."
        })
}

function Get-ExecutableEvidence {
    param (
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($version.FileMajorPart -ne $VisualStudioMajorVersion) {
        throw "$Name at '$Path' has file major $($version.FileMajorPart), not $VisualStudioMajorVersion."
    }

    return [ordered]@{
        Name = $Name
        Path = $Path
        FileVersion = $version.FileVersion
        ProductVersion = $version.ProductVersion
        FileMajor = $version.FileMajorPart
    }
}

function Write-AcceptanceCounts {
    param (
        [Parameter(Mandatory)]
        [string] $TrxPath
    )

    [xml]$trx = Get-Content -LiteralPath $TrxPath -Raw
    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    if (-not $counters -or $results.Count -eq 0) {
        throw "TestAdapter acceptance TRX does not contain counters and test results."
    }

    $outcomes = @($results |
            Group-Object { [string]$_.outcome } |
            Sort-Object Name |
            ForEach-Object {
                [ordered]@{
                    Outcome = $_.Name
                    Count = $_.Count
                }
            })
    $evidence = [ordered]@{
        Total = [int]$counters.total
        Executed = [int]$counters.executed
        Passed = [int]$counters.passed
        Failed = [int]$counters.failed
        Results = $results.Count
        Outcomes = $outcomes
    }
    if ($evidence.Total -le 0 -or $evidence.Executed -le 0) {
        throw "TestAdapter acceptance reported no executed tests."
    }

    Write-Json `
        -Path (Join-Path $diagnosticsDirectory "acceptance-counts.json") `
        -Value $evidence
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$expectedArtifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "_built"))
if ($artifactRoot -ne $expectedArtifactRoot) {
    throw "T11 host validation consumes artifacts only from '$expectedArtifactRoot'."
}

$diagnosticsDirectory = [IO.Path]::GetFullPath($DiagnosticsDirectory)
if ($diagnosticsDirectory.StartsWith(
        $artifactRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "T11 diagnostics must be outside the downloaded artifact root."
}
[void](New-Item -ItemType Directory -Path $diagnosticsDirectory -Force)

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw "LOCALAPPDATA is required for isolated Visual Studio profile evidence."
}
$manifestPath = Join-Path $artifactRoot "t11\canonical-artifacts.json"
$workingDirectory = Join-Path $artifactRoot "t11-host-$RootSuffix"
$acceptanceTargetDirectory = Join-Path $repositoryRoot `
    "src\TestProjects\workspace_with_tests\target"
$acceptanceResultsDirectory = Join-Path $repositoryRoot `
    "src\TestProjects\workspace_with_tests\TestResults"
$validationStartedUtc = [DateTime]::UtcNow
$installerTempDirectory = Join-Path $diagnosticsDirectory `
    "installer-temp-$RootSuffix"
$installerRawLogDirectory = Join-Path $diagnosticsDirectory `
    "installer-configuration-logs"
$installerLogReportPath = Join-Path $diagnosticsDirectory `
    "installer-configuration-logs.json"
$phaseStatus = [ordered]@{
    ArtifactIntegrity = [ordered]@{ Status = "NotRun"; Error = $null }
    HostSelection = [ordered]@{ Status = "NotRun"; Error = $null }
    Isolation = [ordered]@{ Status = "NotRun"; Error = $null }
    Install = [ordered]@{ Status = "NotRun"; Error = $null }
    InstalledIdentity = [ordered]@{ Status = "NotRun"; Error = $null }
    Startup = [ordered]@{ Status = "NotRun"; Error = $null }
    ActivityLog = [ordered]@{ Status = "NotRun"; Error = $null }
    Acceptance = [ordered]@{ Status = "NotRun"; Error = $null }
    Cleanup = [ordered]@{ Status = "NotRun"; Error = $null }
}
$requiredDiagnosticNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
Require-DiagnosticEvidence -Names @(
    "job-context.json",
    "diagnostic-status.json",
    "evidence-matrix.md")

Write-Json `
    -Path (Join-Path $diagnosticsDirectory "job-context.json") `
    -Value ([ordered]@{
        VisualStudioMajorVersion = $VisualStudioMajorVersion
        RootSuffix = $RootSuffix
        ArtifactRoot = $artifactRoot
        DiagnosticsDirectory = $diagnosticsDirectory
        RunnerName = $env:RUNNER_NAME
        RunnerImage = $env:ImageOS
        StartedUtc = $validationStartedUtc.ToString("O")
    })
Write-Json `
    -Path (Join-Path $diagnosticsDirectory "diagnostic-status.json") `
    -Value $phaseStatus
Write-EvidenceMatrix

$minimumVersion = if ($VisualStudioMajorVersion -eq 17) {
    [version]"17.14"
}
else {
    [version]"18.0"
}
$maximumVersion = if ($VisualStudioMajorVersion -eq 17) {
    [version]"18.0"
}
else {
    [version]"19.0"
}

$artifacts = $null
$selectedHost = $null
$vsixIdentity = $null
$installedExtension = $null
$profileOwnership = $null
$installerTempOwnership = $null
$installerRawLogOwnership = $null
$workingDirectoryOwnership = $null
$acceptanceTargetOwnership = $null
$acceptanceResultsOwnership = $null
$installationAttempted = $false
$installerLogCollectionAttempted = $false
$installerLogCollectionError = $null
$installerLogEvidence = $null
$installerProcessResult = $null
$processResults = [Collections.Generic.List[object]]::new()
$requiredInvocationCount = 0
$mainFailure = $null
$currentPhase = $null

try {
    $prerequisiteFailures = [Collections.Generic.List[string]]::new()
    Set-PhaseStatus -Name ArtifactIntegrity -Status Running
    Require-DiagnosticEvidence -Names @("artifact-integrity.json")
    try {
        $artifacts = Test-T11ArtifactTransport `
            -ArtifactRoot $artifactRoot `
            -ManifestPath $manifestPath `
            -ReportPath (Join-Path $diagnosticsDirectory "artifact-integrity.json")
        Set-PhaseStatus -Name ArtifactIntegrity -Status Passed
    }
    catch {
        Set-PhaseStatus `
            -Name ArtifactIntegrity `
            -Status Failed `
            -Error $_.Exception.Message
        $prerequisiteFailures.Add($_.Exception.Message)
    }

    Set-PhaseStatus -Name HostSelection -Status Running
    Require-DiagnosticEvidence -Names @("host-selection.json")
    try {
        $selectedHost = Resolve-T11VisualStudioHost `
            -VisualStudioMajorVersion $VisualStudioMajorVersion `
            -MinimumVersion $minimumVersion `
            -MaximumVersion $maximumVersion `
            -DiagnosticsDirectory $diagnosticsDirectory `
            -ReportPath (Join-Path $diagnosticsDirectory "host-selection.json")
        Require-DiagnosticEvidence -Names @(
            "vswhere-all.json",
            "vswhere-all.stderr.log",
            "vswhere-core-editor.json",
            "vswhere-core-editor.stderr.log")
        Set-PhaseStatus -Name HostSelection -Status Passed
    }
    catch {
        Set-PhaseStatus `
            -Name HostSelection `
            -Status Failed `
            -Error $_.Exception.Message
        $prerequisiteFailures.Add($_.Exception.Message)
    }

    if ($prerequisiteFailures.Count -gt 0) {
        throw ($prerequisiteFailures -join " ")
    }

    $currentPhase = "HostSelection"
    Set-PhaseStatus -Name HostSelection -Status Running
    Require-DiagnosticEvidence -Names @("selected-executables.json")
    $executableEvidence = @(
        Get-ExecutableEvidence `
            -Name "devenv.exe" `
            -Path $selectedHost.DevenvPath
        Get-ExecutableEvidence `
            -Name "VSIXInstaller.exe" `
            -Path $selectedHost.VsixInstallerPath
        Get-ExecutableEvidence `
            -Name "vstest.console.exe" `
            -Path $selectedHost.VSTestPath
    )
    Write-Json `
        -Path (Join-Path $diagnosticsDirectory "selected-executables.json") `
        -Value ([ordered]@{
            InstanceId = $selectedHost.InstanceId
            InstallationVersion = $selectedHost.InstallationVersion
            Executables = $executableEvidence
        })
    Set-PhaseStatus -Name HostSelection -Status Passed

    $currentPhase = "ArtifactIntegrity"
    Set-PhaseStatus -Name ArtifactIntegrity -Status Running
    Require-DiagnosticEvidence -Names @("source-vsix-identity.json")
    $vsixIdentity = Get-T11VsixIdentity -Path $artifacts.MainVsixPath
    if ($vsixIdentity.Id -cne
        "KS.RustAnalyzer.3a91e56b-fb28-4d85-b572-ec964abf8e31" -or
        $vsixIdentity.DisplayName -cne "rust-analyzer.vs") {
        throw "The downloaded main VSIX identity is not the canonical rust-analyzer.vs identity."
    }
    Write-Json `
        -Path (Join-Path $diagnosticsDirectory "source-vsix-identity.json") `
        -Value $vsixIdentity

    $adapterOwner = Split-Path -Parent $artifacts.TestAdapterPath
    $expectedAdapterNames = @(
        & (Join-Path $PSScriptRoot "Get-TestAdapterPackageFile.ps1") `
            -OutputDirectory $adapterOwner |
            ForEach-Object { Split-Path -Leaf $_ })
    if ($expectedAdapterNames.Count -ne 6) {
        throw "The canonical TestAdapter package list must contain exactly six files."
    }
    Require-DiagnosticEvidence -Names @("adapter-members.txt")
    [void](Get-T11AdapterPackageEvidence `
            -Path $artifacts.TestAdapterPath `
            -ExpectedNames $expectedAdapterNames `
            -ReportPath (Join-Path $diagnosticsDirectory "adapter-members.txt"))
    Set-PhaseStatus -Name ArtifactIntegrity -Status Passed

    $currentPhase = "Isolation"
    Set-PhaseStatus -Name Isolation -Status Running
    $profileOwnership = New-T11ProfileOwnership `
        -LocalAppData $env:LOCALAPPDATA `
        -VisualStudioMajorVersion $VisualStudioMajorVersion `
        -InstanceId $selectedHost.InstanceId `
        -RootSuffix $RootSuffix
    $installerTempOwnership = New-T11OwnedDirectory `
        -AnchorPath $diagnosticsDirectory `
        -Path $installerTempDirectory
    [void](Initialize-T11OwnedDirectory `
            -Ownership $installerTempOwnership)
    $installerRawLogOwnership = New-T11OwnedDirectory `
        -AnchorPath $diagnosticsDirectory `
        -Path $installerRawLogDirectory
    [void](Initialize-T11OwnedDirectory `
            -Ownership $installerRawLogOwnership)
    $workingDirectoryOwnership = New-T11OwnedDirectory `
        -AnchorPath $artifactRoot `
        -Path $workingDirectory
    [void](Initialize-T11OwnedDirectory `
            -Ownership $workingDirectoryOwnership)
    $acceptanceTargetOwnership = New-T11OwnedDirectory `
        -AnchorPath $repositoryRoot `
        -Path $acceptanceTargetDirectory
    $acceptanceResultsOwnership = New-T11OwnedDirectory `
        -AnchorPath $repositoryRoot `
        -Path $acceptanceResultsDirectory
    Require-DiagnosticEvidence -Names @("isolation.json")
    Write-Json `
        -Path (Join-Path $diagnosticsDirectory "isolation.json") `
        -Value ([ordered]@{
            ProfilePath = $profileOwnership.OwnedProfilePath
            InstallerTempPath = $installerTempOwnership.Path
            InstallerRawLogPath = $installerRawLogOwnership.Path
            WorkingDirectory = $workingDirectoryOwnership.Path
            AcceptanceTargetDirectory = $acceptanceTargetOwnership.Path
            AcceptanceResultsDirectory = $acceptanceResultsOwnership.Path
        })
    Set-PhaseStatus -Name Isolation -Status Passed

    $currentPhase = "Install"
    Set-PhaseStatus -Name Install -Status Running
    $installationAttempted = $true
    $installArguments = @(
        "/quiet",
        "/norepair",
        "/instanceIds:$($selectedHost.InstanceId)",
        "/rootSuffix:$RootSuffix",
        $artifacts.MainVsixPath)
    $installerEnvironment = [ordered]@{
        TEMP = $installerTempOwnership.Path
        TMP = $installerTempOwnership.Path
    }
    Require-DiagnosticEvidence -Names @(
        "installer-command.json",
        "installer.stdout.log",
        "installer.stderr.log",
        "installer-configuration-logs.json")
    $installerCommandPath = Join-Path $diagnosticsDirectory "installer-command.json"
    Write-PendingProcessEvidence `
        -Path $installerCommandPath `
        -FilePath $selectedHost.VsixInstallerPath `
        -Arguments $installArguments `
        -TimeoutSeconds $ProcessTimeoutSeconds
    $requiredInvocationCount++
    $installerProcessResult = Invoke-T11BoundedProcess `
        -FilePath $selectedHost.VsixInstallerPath `
        -ArgumentList $installArguments `
        -StandardOutputPath (Join-Path $diagnosticsDirectory "installer.stdout.log") `
        -StandardErrorPath (Join-Path $diagnosticsDirectory "installer.stderr.log") `
        -TimeoutSeconds $ProcessTimeoutSeconds `
        -WorkingDirectory $repositoryRoot `
        -EnvironmentVariables $installerEnvironment
    $processResults.Add($installerProcessResult)
    Write-Json -Path $installerCommandPath -Value $installerProcessResult
    $installerLogCollectionAttempted = $true
    if ($installerProcessResult.JobZeroConfirmed) {
        try {
            $installerLogEvidence = Save-T11InstallerLogs `
                -SourceOwnership $installerTempOwnership `
                -RawLogOwnership $installerRawLogOwnership `
                -ReportPath $installerLogReportPath
        }
        catch {
            $installerLogCollectionError = $_.Exception.Message
        }
    }
    else {
        $installerLogCollectionError =
            "Installer logs cannot be collected before confirmed job active-process zero."
        Write-Json `
            -Path $installerLogReportPath `
            -Value ([ordered]@{
                Status = "Failed"
                SourceDirectory = $installerTempOwnership.Path
                Logs = @()
                Error = $installerLogCollectionError
            })
    }
    Assert-ProcessSucceeded `
        -Result $installerProcessResult `
        -Description "VSIX installation"
    if ($installerLogCollectionError) {
        throw $installerLogCollectionError
    }
    Set-PhaseStatus -Name Install -Status Passed

    $currentPhase = "InstalledIdentity"
    Set-PhaseStatus -Name InstalledIdentity -Status Running
    Require-DiagnosticEvidence -Names @("installed-extension.json")
    $installedExtension = Get-T11InstalledExtensionEvidence `
        -Ownership $profileOwnership `
        -ExtensionId $vsixIdentity.Id `
        -ExtensionVersion $vsixIdentity.Version `
        -ReportPath (Join-Path $diagnosticsDirectory "installed-extension.json") `
        -TimeoutSeconds 30
    Set-PhaseStatus -Name InstalledIdentity -Status Passed

    $currentPhase = "Startup"
    Set-PhaseStatus -Name Startup -Status Running
    $activityLogPath = Join-Path $diagnosticsDirectory "ActivityLog.xml"
    $startupArguments = Get-T11ShellStartupArguments `
        -RootSuffix $RootSuffix `
        -ActivityLogPath $activityLogPath
    Require-DiagnosticEvidence -Names @(
        "startup-command.json",
        "startup.stdout.log",
        "startup.stderr.log",
        "ActivityLog.xml")
    $startupCommandPath = Join-Path $diagnosticsDirectory "startup-command.json"
    Write-PendingProcessEvidence `
        -Path $startupCommandPath `
        -FilePath $selectedHost.DevenvPath `
        -Arguments $startupArguments `
        -TimeoutSeconds $ProcessTimeoutSeconds
    $requiredInvocationCount++
    $startupResult = Invoke-T11BoundedProcess `
        -FilePath $selectedHost.DevenvPath `
        -ArgumentList $startupArguments `
        -StandardOutputPath (Join-Path $diagnosticsDirectory "startup.stdout.log") `
        -StandardErrorPath (Join-Path $diagnosticsDirectory "startup.stderr.log") `
        -TimeoutSeconds $ProcessTimeoutSeconds `
        -WorkingDirectory $repositoryRoot
    $processResults.Add($startupResult)
    Write-Json -Path $startupCommandPath -Value $startupResult
    [void](Assert-T11ShellProcessSucceeded -Result $startupResult)
    Set-PhaseStatus -Name Startup -Status Passed

    $currentPhase = "ActivityLog"
    Set-PhaseStatus -Name ActivityLog -Status Running
    Require-DiagnosticEvidence -Names @("activity-log-analysis.json")
    [void](Get-T11ActivityLogAnalysis `
            -ActivityLogPath $activityLogPath `
            -ScopeTokens @(
                $vsixIdentity.Id,
                $vsixIdentity.DisplayName,
                "KS.RustAnalyzer",
                "d879ab25-bd3e-4e01-8b2a-cc60649c016c",
                $installedExtension.ProfilePath,
                $installedExtension.ManifestPath) `
            -ReportPath (Join-Path $diagnosticsDirectory "activity-log-analysis.json"))
    Set-PhaseStatus -Name ActivityLog -Status Passed

    $currentPhase = "Acceptance"
    Set-PhaseStatus -Name Acceptance -Status Running
    $expandedAdapterPath = Join-Path $workingDirectory "TestAdapter"
    [IO.Compression.ZipFile]::ExtractToDirectory(
        $artifacts.TestAdapterPath,
        $expandedAdapterPath)

    $acceptanceScript = Join-Path $repositoryRoot `
        "src\TestProjects\run-integrationtests.ps1"
    $pwshPath = [Environment]::ProcessPath
    $acceptanceArguments = @(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-File",
        $acceptanceScript,
        "-SrcDir",
        (Join-Path $repositoryRoot "src\TestProjects\workspace_with_tests"),
        "-TestAdapterLocation",
        $expandedAdapterPath,
        "-VisualStudioMajorVersion",
        [string]$VisualStudioMajorVersion,
        "-VSTestPath",
        $selectedHost.VSTestPath)
    Require-DiagnosticEvidence -Names @(
        "acceptance-command.json",
        "acceptance.stdout.log",
        "acceptance.stderr.log",
        "acceptance-counts.json",
        "acceptance-results.txt",
        "TestResults.trx")
    $acceptanceCommandPath = Join-Path $diagnosticsDirectory "acceptance-command.json"
    Write-PendingProcessEvidence `
        -Path $acceptanceCommandPath `
        -FilePath $pwshPath `
        -Arguments $acceptanceArguments `
        -TimeoutSeconds $AcceptanceTimeoutSeconds
    $requiredInvocationCount++
    $acceptanceResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList $acceptanceArguments `
        -StandardOutputPath (Join-Path $diagnosticsDirectory "acceptance.stdout.log") `
        -StandardErrorPath (Join-Path $diagnosticsDirectory "acceptance.stderr.log") `
        -TimeoutSeconds $AcceptanceTimeoutSeconds `
        -WorkingDirectory $repositoryRoot
    $processResults.Add($acceptanceResult)
    Write-Json -Path $acceptanceCommandPath -Value $acceptanceResult

    $testResultsDirectory = Join-Path $repositoryRoot `
        "src\TestProjects\workspace_with_tests\TestResults"
    $trxPath = Join-Path $testResultsDirectory "TestResults.trx"
    if (Test-Path -LiteralPath $trxPath -PathType Leaf) {
        $diagnosticTrxPath = Join-Path $diagnosticsDirectory "TestResults.trx"
        Copy-Item -LiteralPath $trxPath -Destination $diagnosticTrxPath
        Write-AcceptanceCounts -TrxPath $diagnosticTrxPath
    }
    $obtainedPath = Join-Path $testResultsDirectory "obtained.txt"
    if (Test-Path -LiteralPath $obtainedPath -PathType Leaf) {
        Copy-Item `
            -LiteralPath $obtainedPath `
            -Destination (Join-Path $diagnosticsDirectory "acceptance-results.txt")
    }
    Assert-ProcessSucceeded `
        -Result $acceptanceResult `
        -Description "TestAdapter acceptance"
    if (-not (Test-Path `
            -LiteralPath (Join-Path $diagnosticsDirectory "acceptance-counts.json") `
            -PathType Leaf)) {
        throw "TestAdapter acceptance did not produce required TRX count evidence."
    }
    Set-PhaseStatus -Name Acceptance -Status Passed
}
catch {
    $mainFailure = $_
    if ($currentPhase -and
        $phaseStatus[$currentPhase].Status -eq "Running") {
        Set-PhaseStatus `
            -Name $currentPhase `
            -Status Failed `
            -Error $_.Exception.Message
    }
}

$cleanupFailures = [Collections.Generic.List[string]]::new()
$cleanupDeletionAllowed = $false
Set-PhaseStatus -Name Cleanup -Status Running
try {
    try {
        [void](Assert-T11CleanupProcessSafety `
                -RequiredInvocationCount $requiredInvocationCount `
                -InvocationResults @($processResults))
        $cleanupDeletionAllowed = $true
    }
    catch {
        $cleanupFailures.Add($_.Exception.Message)
    }

    if ($installationAttempted -and
        -not $installerLogCollectionAttempted -and
        $installerTempOwnership) {
        $installerLogCollectionAttempted = $true
        if ($installerProcessResult -and
            $installerProcessResult.JobZeroConfirmed) {
            try {
                $installerLogEvidence = Save-T11InstallerLogs `
                    -SourceOwnership $installerTempOwnership `
                    -RawLogOwnership $installerRawLogOwnership `
                    -ReportPath $installerLogReportPath
            }
            catch {
                $installerLogCollectionError = $_.Exception.Message
            }
        }
        else {
            $installerLogCollectionError =
                "Installer logs cannot be collected without confirmed job active-process zero."
            Write-Json `
                -Path $installerLogReportPath `
                -Value ([ordered]@{
                    Status = "Failed"
                    SourceDirectory = $installerTempOwnership.Path
                    Logs = @()
                    Error = $installerLogCollectionError
                })
        }
    }
    if ($installerLogCollectionError) {
        $cleanupFailures.Add(
            "Required isolated native installer logs are unavailable: $installerLogCollectionError")
    }

    if ($cleanupDeletionAllowed) {
        if ($profileOwnership) {
            try {
                [void](Remove-T11OwnedProfile -Ownership $profileOwnership)
            }
            catch {
                $cleanupFailures.Add($_.Exception.Message)
            }
        }

        foreach ($ownership in @(
                $installerTempOwnership,
                $workingDirectoryOwnership,
                $acceptanceTargetOwnership,
                $acceptanceResultsOwnership
            )) {
            if (-not $ownership) {
                continue
            }
            try {
                [void](Remove-T11OwnedDirectory -Ownership $ownership)
            }
            catch {
                $cleanupFailures.Add($_.Exception.Message)
            }
        }
    }

    $missingDiagnostics = @($requiredDiagnosticNames | Where-Object {
            -not (Test-Path `
                -LiteralPath (Join-Path $diagnosticsDirectory $_) `
                -PathType Leaf)
        } | Sort-Object)
    if ($missingDiagnostics.Count -gt 0) {
        $cleanupFailures.Add(
            "Required diagnostics are missing: $($missingDiagnostics -join ', ').")
    }
}
catch {
    $cleanupFailures.Add($_.Exception.Message)
}

$cleanupEvidence = [ordered]@{
    Status = if ($cleanupFailures.Count -eq 0) { "Passed" } else { "Failed" }
    Errors = @($cleanupFailures)
    OwnedProfilePath = if ($profileOwnership) {
        $profileOwnership.OwnedProfilePath
    }
    else {
        $null
    }
    ProfileReserved = $profileOwnership -and $profileOwnership.Reserved
    ProfileRemoved = $profileOwnership -and $profileOwnership.Removed
    InstallerTempPath = if ($installerTempOwnership) {
        $installerTempOwnership.Path
    }
    else {
        $null
    }
    InstallerTempRemoved = $installerTempOwnership -and
        $installerTempOwnership.Removed
    InstallerRawLogPath = if ($installerRawLogOwnership) {
        $installerRawLogOwnership.Path
    }
    else {
        $null
    }
    WorkingDirectoryRemoved = $workingDirectoryOwnership -and
        $workingDirectoryOwnership.Removed
    AcceptanceTargetRemoved = $acceptanceTargetOwnership -and
        $acceptanceTargetOwnership.Removed
    AcceptanceResultsRemoved = $acceptanceResultsOwnership -and
        $acceptanceResultsOwnership.Removed
    InstallerLogCount = if ($installerLogEvidence) {
        @($installerLogEvidence.Logs).Count
    }
    else {
        0
    }
    InstallerLogError = $installerLogCollectionError
    RequiredInvocationCount = $requiredInvocationCount
    ReportedInvocationCount = $processResults.Count
    CleanupDeletionAllowed = $cleanupDeletionAllowed
    InvocationJobEvidence = @($processResults | ForEach-Object {
            [ordered]@{
                RootProcessId = $_.RootProcessId
                AssignedBeforeResume = $_.AssignedBeforeResume
                JobZeroConfirmed = $_.JobZeroConfirmed
                ProcessTreeQuiescent = $_.ProcessTreeQuiescent
                TimedOut = $_.TimedOut
                TerminationRequested = $_.TerminationRequested
                CleanupFailed = $_.CleanupFailed
            }
        })
    FinishedUtc = [DateTime]::UtcNow.ToString("O")
}
Write-Json `
    -Path (Join-Path $diagnosticsDirectory "cleanup.json") `
    -Value $cleanupEvidence
if ($cleanupFailures.Count -eq 0) {
    Set-PhaseStatus -Name Cleanup -Status Passed
}
else {
    Set-PhaseStatus `
        -Name Cleanup `
        -Status Failed `
        -Error ($cleanupFailures -join " ")
}
Write-EvidenceMatrix

$failures = [Collections.Generic.List[string]]::new()
if ($mainFailure) {
    $failures.Add($mainFailure.Exception.Message)
}
foreach ($failure in $cleanupFailures) {
    $failures.Add($failure)
}
if ($failures.Count -gt 0) {
    throw "T11 Visual Studio $VisualStudioMajorVersion host validation failed: $($failures -join ' ')"
}

Write-Host "T11 Visual Studio $VisualStudioMajorVersion host validation passed."
