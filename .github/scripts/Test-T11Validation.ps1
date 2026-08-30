#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "T11Validation.psm1") -Force

$assertionCount = 0

function Assert-True {
    param (
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
    $script:assertionCount++
}

function Assert-Throws {
    param (
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $MessagePattern
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "Expected failure matching '$MessagePattern'; got '$($_.Exception.Message)'."
        }
        $script:assertionCount++
        return
    }
    throw "Expected failure matching '$MessagePattern', but the action succeeded."
}

function New-TestZip {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [Collections.IDictionary] $Entries
    )

    $stream = [IO.File]::Create($Path)
    $archive = [IO.Compression.ZipArchive]::new(
        $stream,
        [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Entries.GetEnumerator()) {
            $entry = $archive.CreateEntry([string]$item.Key)
            $entryStream = $entry.Open()
            $writer = [IO.StreamWriter]::new(
                $entryStream,
                [Text.UTF8Encoding]::new($false))
            try {
                $writer.Write([string]$item.Value)
            }
            finally {
                $writer.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

function Write-TestActivityLog {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $escapedDescription = [Security.SecurityElement]::Escape($Description)
    [IO.File]::WriteAllText(
        $Path,
        "<activity><entry><record>1</record><type>Error</type><source>VisualStudio</source><description>$escapedDescription</description></entry></activity>",
        [Text.UTF8Encoding]::new($false))
}

function New-FakeVisualStudioInstance {
    param (
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $InstanceId,

        [Parameter(Mandatory)]
        [string] $Version,

        [bool] $IsComplete = $true,

        [bool] $IsLaunchable = $true
    )

    $installationPath = Join-Path $Root $InstanceId
    $devenvPath = Join-Path $installationPath "Common7\IDE\devenv.exe"
    $vsixInstallerPath = Join-Path $installationPath "Common7\IDE\VSIXInstaller.exe"
    $vstestPath = Join-Path $installationPath `
        "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
    foreach ($path in @($devenvPath, $vsixInstallerPath, $vstestPath)) {
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
        [IO.File]::WriteAllBytes($path, [byte[]](1))
    }

    return [pscustomobject]@{
        instanceId = $InstanceId
        installationPath = $installationPath
        installationVersion = $Version
        productId = "Microsoft.VisualStudio.Product.Enterprise"
        productPath = $devenvPath
        isComplete = $IsComplete
        isLaunchable = $IsLaunchable
    }
}

function Get-WorkflowJob {
    param (
        [Parameter(Mandatory)]
        [string] $Workflow,

        [Parameter(Mandatory)]
        [string] $JobId
    )

    $match = [regex]::Match(
        $Workflow,
        "(?ms)^  $([regex]::Escape($JobId)):\r?\n(?<Body>.*?)(?=^  [A-Za-z0-9_-]+:\r?\n|\z)")
    if (-not $match.Success) {
        throw "Workflow job '$JobId' was not found."
    }
    return $match.Value
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$testRoot = Join-Path $repositoryRoot "_built\script-tests\$([guid]::NewGuid())"
[void](New-Item -ItemType Directory -Path $testRoot -Force)

try {
    $artifactRoot = Join-Path $testRoot "transport"
    $reportRoot = Join-Path $testRoot "reports"
    [void](New-Item -ItemType Directory -Path $reportRoot -Force)
    $definitions = @(Get-T11ArtifactDefinitions)
    foreach ($definition in $definitions) {
        $path = Join-Path $artifactRoot $definition.RelativePath
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
        [IO.File]::WriteAllBytes(
            $path,
            [Text.Encoding]::UTF8.GetBytes("canonical-$($definition.Name)"))
    }
    $manifestPath = Join-Path $artifactRoot "t11\canonical-artifacts.json"
    [void](New-T11ArtifactManifest `
            -ArtifactRoot $artifactRoot `
            -ManifestPath $manifestPath)
    $verified = Test-T11ArtifactTransport `
        -ArtifactRoot $artifactRoot `
        -ManifestPath $manifestPath `
        -ReportPath (Join-Path $reportRoot "valid-artifacts.json")
    Assert-True `
        -Condition ($verified.MainVsixPath.EndsWith("RustAnalyzer.vsix")) `
        -Message "Valid producer artifact records were not accepted."

    $mainVsixPath = $verified.MainVsixPath
    [IO.File]::WriteAllBytes(
        $mainVsixPath,
        [Text.Encoding]::UTF8.GetBytes("tampered"))
    Assert-Throws `
        -Action {
            [void](Test-T11ArtifactTransport `
                    -ArtifactRoot $artifactRoot `
                    -ManifestPath $manifestPath `
                    -ReportPath (Join-Path $reportRoot "tampered-artifacts.json"))
        } `
        -MessagePattern "do not match"

    [IO.File]::WriteAllBytes(
        $mainVsixPath,
        [Text.Encoding]::UTF8.GetBytes("canonical-MainVsix"))
    $unexpectedPath = Join-Path $artifactRoot "unexpected.bin"
    [IO.File]::WriteAllBytes($unexpectedPath, [byte[]](1))
    Assert-Throws `
        -Action {
            [void](Test-T11ArtifactTransport `
                    -ArtifactRoot $artifactRoot `
                    -ManifestPath $manifestPath `
                    -ReportPath (Join-Path $reportRoot "unexpected-artifacts.json"))
        } `
        -MessagePattern "unexpected files"
    Remove-Item -LiteralPath $unexpectedPath -Force

    $fakeVsRoot = Join-Path $testRoot "visual-studio"
    $instanceA = New-FakeVisualStudioInstance `
        -Root $fakeVsRoot `
        -InstanceId "instance-a" `
        -Version "17.14.100.0"
    $selection = Get-T11HostSelection `
        -Instances @($instanceA) `
        -CoreEditorInstanceIds @($instanceA.instanceId) `
        -VisualStudioMajorVersion 17 `
        -MinimumVersion ([version]"17.14") `
        -MaximumVersion ([version]"18.0")
    Assert-True `
        -Condition ($selection.Candidates.Count -eq 1) `
        -Message "One qualifying Visual Studio host was not selected."

    $instanceB = New-FakeVisualStudioInstance `
        -Root $fakeVsRoot `
        -InstanceId "instance-b" `
        -Version "17.14.200.0"
    $ambiguous = Get-T11HostSelection `
        -Instances @($instanceA, $instanceB) `
        -CoreEditorInstanceIds @($instanceA.instanceId, $instanceB.instanceId) `
        -VisualStudioMajorVersion 17 `
        -MinimumVersion ([version]"17.14") `
        -MaximumVersion ([version]"18.0")
    Assert-True `
        -Condition ($ambiguous.Candidates.Count -eq 2) `
        -Message "Ambiguous qualifying Visual Studio hosts were not preserved for fail-closed resolution."

    $incomplete = New-FakeVisualStudioInstance `
        -Root $fakeVsRoot `
        -InstanceId "instance-incomplete" `
        -Version "18.1.0.0" `
        -IsComplete $false
    $rejected = Get-T11HostSelection `
        -Instances @($incomplete) `
        -CoreEditorInstanceIds @($incomplete.instanceId) `
        -VisualStudioMajorVersion 18 `
        -MinimumVersion ([version]"18.0") `
        -MaximumVersion ([version]"19.0")
    Assert-True `
        -Condition (
            $rejected.Candidates.Count -eq 0 -and
            $rejected.Decisions[0].RejectionReasons -contains
                "Installation is incomplete.") `
        -Message "An incomplete Visual Studio host was not rejected."

    $vsixPath = Join-Path $testRoot "identity.vsix"
    New-TestZip `
        -Path $vsixPath `
        -Entries ([ordered]@{
            "extension.vsixmanifest" = @"
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Id="test.extension" Version="1.2.3" />
    <DisplayName>Test extension</DisplayName>
  </Metadata>
</PackageManifest>
"@
        })
    $identity = Get-T11VsixIdentity -Path $vsixPath
    Assert-True `
        -Condition (
            $identity.Id -ceq "test.extension" -and
            $identity.Version -ceq "1.2.3") `
        -Message "VSIX identity evidence was not read exactly."

    $adapterPath = Join-Path $testRoot "adapter.zip"
    $adapterNames = @("a.dll", "a.pdb", "b.dll", "b.pdb", "c.dll", "d.dll")
    $adapterEntries = [ordered]@{}
    foreach ($name in $adapterNames) {
        $adapterEntries[$name] = $name
    }
    New-TestZip -Path $adapterPath -Entries $adapterEntries
    $adapterEvidence = @(Get-T11AdapterPackageEvidence `
            -Path $adapterPath `
            -ExpectedNames $adapterNames `
            -ReportPath (Join-Path $reportRoot "adapter-members.txt"))
    Assert-True `
        -Condition ($adapterEvidence.Count -eq 6) `
        -Message "The exact six-member TestAdapter archive was not accepted."

    $badAdapterPath = Join-Path $testRoot "bad-adapter.zip"
    $badAdapterEntries = [ordered]@{}
    foreach ($name in $adapterNames) {
        $badAdapterEntries[$name] = $name
    }
    $badAdapterEntries["extra.dll"] = "extra"
    New-TestZip -Path $badAdapterPath -Entries $badAdapterEntries
    $badAdapterReportPath = Join-Path $reportRoot "bad-adapter-members.txt"
    Assert-Throws `
        -Action {
            [void](Get-T11AdapterPackageEvidence `
                    -Path $badAdapterPath `
                    -ExpectedNames $adapterNames `
                    -ReportPath $badAdapterReportPath)
        } `
        -MessagePattern "membership"
    $badAdapterReport = Get-Content -LiteralPath $badAdapterReportPath -Raw
    Assert-True `
        -Condition (
            $badAdapterReport -match "(?m)^Status: Failed\r?$" -and
            $badAdapterReport -match "(?m)^Error: .+\r?$" -and
            $badAdapterReport -match "(?m)^Expected:\r?$" -and
            $badAdapterReport -match "(?m)^Actual:\r?$" -and
            $badAdapterReport -match "(?m)^Missing:\r?$" -and
            $badAdapterReport -match "(?m)^Extra:\r?$" -and
            $badAdapterReport -match "(?m)^DuplicateOrInvalid:\r?$" -and
            $badAdapterReport -match "(?m)^extra\.dll(?:`t|$)") `
        -Message "Failed TestAdapter membership evidence did not name extra.dll."

    $activityLogPath = Join-Path $testRoot "ActivityLog.xml"
    $irrelevantActivityCases = @(
        "KS.RustAnalyzer optional update feed unavailable",
        "KS.RustAnalyzer package update failed to load release metadata",
        "KS.RustAnalyzer update package could not load the release feed",
        "KS.RustAnalyzer package restore cannot load optional index",
        "KS.RustAnalyzer package failed to load release metadata")
    for ($index = 0; $index -lt $irrelevantActivityCases.Count; $index++) {
        Write-TestActivityLog `
            -Path $activityLogPath `
            -Description $irrelevantActivityCases[$index]
        $irrelevantActivityReportPath = Join-Path $reportRoot `
            "activity-scoped-irrelevant-$index.json"
        $irrelevantActivity = Get-T11ActivityLogAnalysis `
            -ActivityLogPath $activityLogPath `
            -ScopeTokens @("KS.RustAnalyzer") `
            -ReportPath $irrelevantActivityReportPath
        Assert-True `
            -Condition (
                (Test-Path `
                    -LiteralPath $irrelevantActivityReportPath `
                    -PathType Leaf) -and
                $irrelevantActivity.Status -eq "Passed" -and
                $irrelevantActivity.ScopedErrors.Count -eq 1 -and
                $irrelevantActivity.BlockingErrorCount -eq 0 -and
                -not $irrelevantActivity.ScopedErrors[0].BlocksValidation) `
            -Message "A scoped irrelevant Activity Log error blocked validation."
    }

    $blockingActivityCases = @(
        [pscustomobject]@{
            Name = "Registration"
            Category = "Registration"
            Description = "KS.RustAnalyzer registration failed"
        },
        [pscustomobject]@{
            Name = "Composition"
            Category = "Composition"
            Description = "KS.RustAnalyzer MEF composition failure"
        },
        [pscustomobject]@{
            Name = "Binding"
            Category = "Binding"
            Description = "KS.RustAnalyzer could not load file or assembly Dependency"
        },
        [pscustomobject]@{
            Name = "PackageLoadFailure"
            Category = "PackageLoad"
            Description = "KS.RustAnalyzer Package Load Failure"
        },
        [pscustomobject]@{
            Name = "PackageSetSite"
            Category = "PackageLoad"
            Description = "KS.RustAnalyzer SetSite failed for package"
        },
        [pscustomobject]@{
            Name = "PackageCreateInstance"
            Category = "PackageLoad"
            Description = "KS.RustAnalyzer CreateInstance failed for package"
        },
        [pscustomobject]@{
            Name = "ScopedPackage"
            Category = "PackageLoad"
            Description = "KS.RustAnalyzer package failed to load"
        })
    foreach ($case in $blockingActivityCases) {
        Write-TestActivityLog `
            -Path $activityLogPath `
            -Description $case.Description
        $activityReportPath = Join-Path $reportRoot `
            "activity-$($case.Name).json"
        Assert-Throws `
            -Action {
                [void](Get-T11ActivityLogAnalysis `
                        -ActivityLogPath $activityLogPath `
                        -ScopeTokens @("KS.RustAnalyzer") `
                        -ReportPath $activityReportPath)
            } `
            -MessagePattern "approved main-extension fault"
        $activityReport = Get-Content -LiteralPath $activityReportPath -Raw |
            ConvertFrom-Json
        Assert-True `
            -Condition (
                $activityReport.BlockingErrorCount -eq 1 -and
                $activityReport.BlockingErrors[0].Category -eq $case.Category -and
                $activityReport.BlockingErrors[0].BlocksValidation) `
            -Message "Activity Log $($case.Name) failure was not classified."
    }

    Write-TestActivityLog `
        -Path $activityLogPath `
        -Description "Unrelated host error"
    $cleanActivity = Get-T11ActivityLogAnalysis `
        -ActivityLogPath $activityLogPath `
        -ScopeTokens @("KS.RustAnalyzer") `
        -ReportPath (Join-Path $reportRoot "activity-clean.json")
    Assert-True `
        -Condition ($cleanActivity.Status -eq "Passed") `
        -Message "An unscoped Activity Log error failed validation."

    $profileParent = Join-Path $testRoot "profiles"
    $profilePath = Join-Path $profileParent "17.0_instanceT11TEST"
    $installedManifestPath = Join-Path $profilePath `
        "Extensions\publisher\extension.vsixmanifest"
    [void](New-Item `
        -ItemType Directory `
        -Path (Split-Path -Parent $installedManifestPath) `
        -Force)
    [IO.File]::WriteAllText(
        $installedManifestPath,
        @"
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata><Identity Id="test.extension" Version="1.2.3" /></Metadata>
</PackageManifest>
"@,
        [Text.UTF8Encoding]::new($false))
    $installed = Get-T11InstalledExtensionEvidence `
        -ProfileParent $profileParent `
        -RootSuffix "T11TEST" `
        -ExtensionId "test.extension" `
        -ExtensionVersion "1.2.3" `
        -ReportPath (Join-Path $reportRoot "installed-extension.json") `
        -TimeoutSeconds 1
    Assert-True `
        -Condition ($installed.ManifestPath -eq $installedManifestPath) `
        -Message "Installed extension identity/version evidence was not found."

    $ownershipParent = Join-Path $testRoot "profile-ownership"
    $ownershipSuffix = "T11OWNED"
    $preExistingProfile = Join-Path $ownershipParent `
        "17.0_preexisting$ownershipSuffix"
    [void](New-Item -ItemType Directory -Path $preExistingProfile -Force)
    Assert-Throws `
        -Action {
            [void](New-T11ProfileOwnership `
                    -ProfileParent $ownershipParent `
                    -RootSuffix $ownershipSuffix)
        } `
        -MessagePattern "already exists"
    Assert-True `
        -Condition (Test-Path `
            -LiteralPath $preExistingProfile `
            -PathType Container) `
        -Message "A colliding pre-existing Visual Studio profile was removed."
    Remove-Item -LiteralPath $preExistingProfile -Recurse -Force

    $ownership = New-T11ProfileOwnership `
        -ProfileParent $ownershipParent `
        -RootSuffix $ownershipSuffix
    $runOwnedProfile = Join-Path $ownershipParent `
        "17.0_run$ownershipSuffix"
    $unownedProfile = Join-Path $ownershipParent `
        "18.0_unowned$ownershipSuffix"
    [void](New-Item -ItemType Directory -Path $runOwnedProfile -Force)
    [void](New-Item -ItemType Directory -Path $unownedProfile -Force)
    $removedWithoutOwnership = Remove-T11OwnedProfile -Ownership $ownership
    Assert-True `
        -Condition (
            -not $removedWithoutOwnership -and
            (Test-Path -LiteralPath $runOwnedProfile -PathType Container) -and
            (Test-Path -LiteralPath $unownedProfile -PathType Container)) `
        -Message "Profile cleanup ran before exact ownership was established."

    [void](Set-T11OwnedProfile `
            -Ownership $ownership `
            -ProfilePath $runOwnedProfile)
    $removedOwnedProfile = Remove-T11OwnedProfile -Ownership $ownership
    Assert-True `
        -Condition (
            $removedOwnedProfile -and
            -not (Test-Path -LiteralPath $runOwnedProfile) -and
            (Test-Path -LiteralPath $unownedProfile -PathType Container) -and
            $ownership.Removed) `
        -Message "Profile cleanup did not remove only the exact run-owned profile."

    $pwshPath = (Get-Process -Id $PID).Path
    $successResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @("-NoProfile", "-Command", "'bounded-success'") `
        -StandardOutputPath (Join-Path $reportRoot "process-success.stdout.log") `
        -StandardErrorPath (Join-Path $reportRoot "process-success.stderr.log") `
        -TimeoutSeconds 10
    Assert-True `
        -Condition (
            -not $successResult.TimedOut -and
            $successResult.ExitCode -eq 0) `
        -Message "A successful bounded process did not complete cleanly."

    $timeoutResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @(
            "-NoProfile",
            "-Command",
            "Start-Sleep -Seconds 30") `
        -StandardOutputPath (Join-Path $reportRoot "process-timeout.stdout.log") `
        -StandardErrorPath (Join-Path $reportRoot "process-timeout.stderr.log") `
        -TimeoutSeconds 1
    Assert-True `
        -Condition (
            $timeoutResult.TimedOut -and
            -not (Get-Process `
                -Id $timeoutResult.ProcessId `
                -ErrorAction SilentlyContinue)) `
        -Message "A timed-out process was not stopped by exact PID."

    $workflowPath = Join-Path $repositoryRoot ".github\workflows\cdp.yml"
    $workflow = Get-Content -LiteralPath $workflowPath -Raw
    $producerJob = Get-WorkflowJob -Workflow $workflow -JobId "build-and-test"
    $hostVs17Job = Get-WorkflowJob -Workflow $workflow -JobId "host-vs17"
    $hostVs18Job = Get-WorkflowJob -Workflow $workflow -JobId "host-vs18"
    $publishJob = Get-WorkflowJob -Workflow $workflow -JobId "publish"

    Assert-True `
        -Condition (
            ([regex]::Matches($workflow, "Invoke-Build\.ps1")).Count -eq 1 -and
            ([regex]::Matches($workflow, "New-TestAdapterPackage\.ps1")).Count -eq 1 -and
            ([regex]::Matches($workflow, "New-T11ArtifactManifest\.ps1")).Count -eq 1) `
        -Message "The workflow must build, pack, and record canonical artifacts exactly once."
    Assert-True `
        -Condition (
            $producerJob -match "runs-on: windows-2022" -and
            $producerJob -match "-VisualStudioMajorVersion 17" -and
            $producerJob -match [regex]::Escape("_built\t11\canonical-artifacts.json") -and
            $producerJob -match [regex]::Escape('_built\projects\RustAnalyzer\${{ env.VsixFileName }}') -and
            $producerJob -match [regex]::Escape('_built\projects\RustAnalyzer.TestAdapter\${{ env.TestAdapterNameNoExt }}.zip')) `
        -Message "The VS17 producer contract is incomplete."
    Assert-True `
        -Condition (
            $hostVs17Job -match "runs-on: windows-2022" -and
            $hostVs17Job -match "-VisualStudioMajorVersion 17" -and
            $hostVs17Job -match "timeout-minutes: 45" -and
            $hostVs18Job -match "runs-on: windows-2025-vs2026" -and
            $hostVs18Job -match "-VisualStudioMajorVersion 18" -and
            $hostVs18Job -match "timeout-minutes: 45") `
        -Message "The blocking dual-host runner/major contract is incomplete."
    Assert-True `
        -Condition (
            $hostVs17Job -notmatch "Invoke-Build|New-TestAdapterPackage" -and
            $hostVs18Job -notmatch "Invoke-Build|New-TestAdapterPackage" -and
            $hostVs17Job -match [regex]::Escape('${{ env.T11HostArtifact }}') -and
            $hostVs18Job -match [regex]::Escape('${{ env.T11HostArtifact }}') -and
            $hostVs17Job -notmatch "BuildOutputArtifact" -and
            $hostVs18Job -notmatch "BuildOutputArtifact" -and
            $hostVs17Job -match "t11-host-vs17-diagnostics" -and
            $hostVs18Job -match "t11-host-vs18-diagnostics" -and
            ([regex]::Matches($hostVs17Job, "if: always\(\)")).Count -ge 2 -and
            ([regex]::Matches($hostVs18Job, "if: always\(\)")).Count -ge 2) `
        -Message "Host jobs must consume without rebuilding and always upload unique diagnostics."
    Assert-True `
        -Condition (
            $workflow -notmatch "(?m)^  acceptance:" -and
            $publishJob -match "host-vs17" -and
            $publishJob -match "host-vs18" -and
            $publishJob -notmatch "\bacceptance\b" -and
            $workflow -notmatch "continue-on-error") `
        -Message "Publication is not exclusively gated by both blocking host jobs."
    Assert-True `
        -Condition ($workflow -notmatch "RustDevelopmentPack") `
        -Message "The T11 workflow must not transport or promote the Development Pack."

    $hostScript = Get-Content `
        -LiteralPath (Join-Path $PSScriptRoot "Invoke-T11HostValidation.ps1") `
        -Raw
    $module = Get-Content `
        -LiteralPath (Join-Path $PSScriptRoot "T11Validation.psm1") `
        -Raw
    $isolationPhase = [regex]::Match(
        $hostScript,
        '(?s)\$currentPhase = "Isolation".*?(?=\$currentPhase = "Install")')
    Assert-True `
        -Condition (
            $isolationPhase.Success -and
            $isolationPhase.Value -match
                'Set-PhaseStatus -Name Isolation -Status Running' -and
            $isolationPhase.Value -match 'New-T11ProfileOwnership' -and
            $isolationPhase.Value -match
                'Set-PhaseStatus -Name Isolation -Status Passed' -and
            $isolationPhase.Value -notmatch
                '\$currentPhase = \$null|installer-command\.json' -and
            $hostScript -match
                'Isolation = \[ordered\]@\{ Status = "NotRun"; Error = \$null \}' -and
            $hostScript -match
                '(?s)-Name \$currentPhase.*?-Status Failed.*?-Error \$_.Exception.Message' -and
            $hostScript -match
                '\$isolationEvidence = if \(\$phaseStatus\.Isolation\.Error\)' -and
            $hostScript -match
                '\| Isolation ownership \| \$isolationEvidence \| \$\(\$phaseStatus\.Isolation\.Status\) \|') `
        -Message "Profile collisions are not recorded as isolated phase failures."
    Assert-True `
        -Condition (
            $hostScript -match
                'Require-DiagnosticEvidence -Names @\("adapter-members\.txt"\)' -and
            $hostScript -notmatch 'if \(-not \$mainFailure\)' -and
            $hostScript -match
                '\$requiredDiagnosticNames \| Where-Object') `
        -Message "Failure-path diagnostics are not required only when reached."
    Assert-True `
        -Condition (
            ($hostScript + $module) -notmatch
                "(?i)Stop-Process\s+-Name|taskkill|/shutdownprocesses|VSIXInstaller\.exe\s+/\?" -and
            $hostScript -notmatch "Compress-Archive|New-TestAdapterPackage" -and
            $hostScript -notmatch "Get-IsolatedProfiles" -and
            $hostScript -match "Remove-T11OwnedProfile" -and
            $hostScript -match "/instanceIds:" -and
            $hostScript -match "/rootSuffix:" -and
            $hostScript -match '"File\.Exit"' -and
            $hostScript -match "Not in T11 - T13" -and
            $hostScript -match "Not in T11 - T14") `
        -Message "T11 host validation violates process, targeting, or evidence-matrix scope."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host "T11 validation tests passed: $assertionCount assertions."
