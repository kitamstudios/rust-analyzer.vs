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

function Write-TestScript {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Content
    )

    [IO.File]::WriteAllText(
        $Path,
        $Content,
        [Text.UTF8Encoding]::new($false))
}

function New-TestOwnedDirectory {
    param (
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $anchor = Join-Path $Root $Name
    [void](New-Item -ItemType Directory -Path $anchor -Force)
    $ownership = New-T11OwnedDirectory `
        -AnchorPath $anchor `
        -Path (Join-Path $anchor "owned")
    [void](Initialize-T11OwnedDirectory -Ownership $ownership)
    return $ownership
}

function New-TestProfile {
    param (
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Name,

        [string] $RootSuffix = "T11TEST",

        [string] $InstanceId = "abc12345"
    )

    $localAppData = Join-Path $Root "$Name\LocalAppData"
    [void](New-Item `
        -ItemType Directory `
        -Path (Join-Path $localAppData "Microsoft\VisualStudio") `
        -Force)
    return New-T11ProfileOwnership `
        -LocalAppData $localAppData `
        -VisualStudioMajorVersion 17 `
        -InstanceId $InstanceId `
        -RootSuffix $RootSuffix
}

function Write-TestInstalledManifest {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership,

        [string] $ExtensionDirectoryName = "publisher",

        [string] $Content = @"
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata><Identity Id="test.extension" Version="1.2.3" /></Metadata>
</PackageManifest>
"@
    )

    $extensionDirectory = Join-Path `
        $Ownership.OwnedProfilePath `
        "Extensions\$ExtensionDirectoryName"
    [void](New-Item -ItemType Directory -Path $extensionDirectory -Force)
    $manifestPath = Join-Path $extensionDirectory "extension.vsixmanifest"
    [IO.File]::WriteAllText(
        $manifestPath,
        $Content,
        [Text.UTF8Encoding]::new($false))
    return $manifestPath
}

function Get-TestInstalledEvidence {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership,

        [Parameter(Mandatory)]
        [string] $ReportPath,

        [string] $ExtensionId = "test.extension",

        [string] $ExtensionVersion = "1.2.3",

        [int] $MaximumManifestByteLength = 1048576
    )

    return Get-T11InstalledExtensionEvidence `
        -Ownership $Ownership `
        -ExtensionId $ExtensionId `
        -ExtensionVersion $ExtensionVersion `
        -ReportPath $ReportPath `
        -TimeoutSeconds 1 `
        -MaximumManifestByteLength $MaximumManifestByteLength
}

function New-TestJunction {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Target
    )

    [void](New-Item -ItemType Directory -Path $Target -Force)
    return New-Item -ItemType Junction -Path $Path -Target $Target
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

    $installerLogRoot = Join-Path $testRoot "installer-logs"
    [void](New-Item -ItemType Directory -Path $installerLogRoot -Force)
    [IO.File]::WriteAllBytes(
        (Join-Path $installerLogRoot "dd_VSIXInstaller_ambient.log"),
        [byte[]](1))

    $zeroLogOwnership = New-TestOwnedDirectory `
        -Root $installerLogRoot `
        -Name "zero"
    $zeroRawOwnership = New-TestOwnedDirectory `
        -Root $reportRoot `
        -Name "raw-zero"
    $zeroLogReport = Join-Path $reportRoot "installer-logs-zero.json"
    Assert-Throws `
        -Action {
            [void](Save-T11InstallerLogs `
                    -SourceOwnership $zeroLogOwnership `
                    -RawLogOwnership $zeroRawOwnership `
                    -ReportPath $zeroLogReport)
        } `
        -MessagePattern "no native logs"
    $zeroLogEvidence = Get-Content -LiteralPath $zeroLogReport -Raw |
        ConvertFrom-Json
    Assert-True `
        -Condition (
            $zeroLogEvidence.Status -eq "Failed" -and
            $zeroLogEvidence.Logs.Count -eq 0) `
        -Message "Missing native installer logs did not fail with diagnostics."
    [void](Remove-T11OwnedDirectory -Ownership $zeroLogOwnership)

    $emptyLogOwnership = New-TestOwnedDirectory `
        -Root $installerLogRoot `
        -Name "empty"
    $emptyRawOwnership = New-TestOwnedDirectory `
        -Root $reportRoot `
        -Name "raw-empty"
    [IO.File]::WriteAllBytes(
        (Join-Path $emptyLogOwnership.Path "dd_VSIXInstaller_empty.log"),
        [byte[]]::new(0))
    Assert-Throws `
        -Action {
            [void](Save-T11InstallerLogs `
                    -SourceOwnership $emptyLogOwnership `
                    -RawLogOwnership $emptyRawOwnership `
                    -ReportPath (Join-Path $reportRoot "installer-logs-empty.json"))
        } `
        -MessagePattern "empty native logs"
    [void](Remove-T11OwnedDirectory -Ownership $emptyLogOwnership)

    $nestedLogOwnership = New-TestOwnedDirectory `
        -Root $installerLogRoot `
        -Name "nested"
    $nestedRawOwnership = New-TestOwnedDirectory `
        -Root $reportRoot `
        -Name "raw-nested"
    $nestedLogDirectory = Join-Path $nestedLogOwnership.Path "worker"
    [void](New-Item -ItemType Directory -Path $nestedLogDirectory)
    [IO.File]::WriteAllBytes(
        (Join-Path $nestedLogDirectory "dd_VSIXInstaller_nested.log"),
        [byte[]](1))
    Assert-Throws `
        -Action {
            [void](Save-T11InstallerLogs `
                    -SourceOwnership $nestedLogOwnership `
                    -RawLogOwnership $nestedRawOwnership `
                    -ReportPath (Join-Path $reportRoot "installer-logs-nested.json"))
        } `
        -MessagePattern "direct children"
    [void](Remove-T11OwnedDirectory -Ownership $nestedLogOwnership)

    $reparseLogOwnership = New-TestOwnedDirectory `
        -Root $installerLogRoot `
        -Name "reparse"
    $reparseRawOwnership = New-TestOwnedDirectory `
        -Root $reportRoot `
        -Name "raw-reparse"
    $reparseLogPath = Join-Path `
        $reparseLogOwnership.Path `
        "dd_VSIXInstaller_link.log"
    [void](New-TestJunction `
            -Path $reparseLogPath `
            -Target (Join-Path $installerLogRoot "reparse-target"))
    Assert-Throws `
        -Action {
            [void](Save-T11InstallerLogs `
                    -SourceOwnership $reparseLogOwnership `
                    -RawLogOwnership $reparseRawOwnership `
                    -ReportPath (Join-Path $reportRoot "installer-logs-reparse.json"))
        } `
        -MessagePattern "reparse point"
    Remove-Item -LiteralPath $reparseLogPath -Force
    [void](Remove-T11OwnedDirectory -Ownership $reparseLogOwnership)

    $multipleLogOwnership = New-TestOwnedDirectory `
        -Root $installerLogRoot `
        -Name "multiple"
    $identicalLogBytes = [byte[]](0xFF, 0x00, 0xFE, 0x01)
    $sourceLogs = @(
        Join-Path $multipleLogOwnership.Path "dd_VSIXInstaller_a.log"
        Join-Path $multipleLogOwnership.Path "dd_VSIXInstaller_b.log")
    foreach ($sourceLog in $sourceLogs) {
        [IO.File]::WriteAllBytes($sourceLog, $identicalLogBytes)
    }
    [IO.File]::WriteAllBytes(
        (Join-Path $multipleLogOwnership.Path "unrelated.bin"),
        [byte[]](2))
    $multipleRawOwnership = New-TestOwnedDirectory `
        -Root $reportRoot `
        -Name "raw-multiple"
    $multipleRawDirectory = $multipleRawOwnership.Path
    $multipleLogReport = Join-Path $reportRoot "installer-logs-multiple.json"
    $multipleLogEvidence = Save-T11InstallerLogs `
        -SourceOwnership $multipleLogOwnership `
        -RawLogOwnership $multipleRawOwnership `
        -ReportPath $multipleLogReport
    $firstManifest = Get-Content -LiteralPath $multipleLogReport -Raw
    [void](Save-T11InstallerLogs `
            -SourceOwnership $multipleLogOwnership `
            -RawLogOwnership $multipleRawOwnership `
            -ReportPath $multipleLogReport)
    $secondManifest = Get-Content -LiteralPath $multipleLogReport -Raw
    $rawLogs = @(Get-ChildItem -LiteralPath $multipleRawDirectory -File)
    Assert-True `
        -Condition (
            $multipleLogEvidence.Logs.Count -eq 2 -and
            $rawLogs.Count -eq 2 -and
            $multipleLogEvidence.Logs[0].Sha256 -ceq
                $multipleLogEvidence.Logs[1].Sha256 -and
            $firstManifest -ceq $secondManifest) `
        -Message "Distinct identical native logs were not preserved idempotently: records=$($multipleLogEvidence.Logs.Count), raw=$($rawLogs.Count), hashes=$($multipleLogEvidence.Logs[0].Sha256 -ceq $multipleLogEvidence.Logs[1].Sha256), manifest=$($firstManifest -ceq $secondManifest)."
    foreach ($record in $multipleLogEvidence.Logs) {
        Assert-True `
            -Condition (
                -not [string]::IsNullOrWhiteSpace($record.OriginalPath) -and
                -not [string]::IsNullOrWhiteSpace($record.OriginalName) -and
                -not [string]::IsNullOrWhiteSpace($record.CreationTimeUtc) -and
                -not [string]::IsNullOrWhiteSpace($record.LastWriteTimeUtc) -and
                (Get-FileHash `
                    -LiteralPath $record.EvidencePath `
                    -Algorithm SHA256).Hash -ceq $record.Sha256 -and
                (Get-Item -LiteralPath $record.EvidencePath).Length -eq
                    $record.ByteLength) `
            -Message "Raw installer bytes and recorded hash/length differ."
    }
    $preservedBytes = [IO.File]::ReadAllBytes(
        $multipleLogEvidence.Logs[0].EvidencePath)
    [IO.File]::WriteAllBytes($sourceLogs[0], [byte[]](9, 9, 9))
    Assert-True `
        -Condition (
            [Convert]::ToHexString($preservedBytes) -ceq
                [Convert]::ToHexString([IO.File]::ReadAllBytes(
                        $multipleLogEvidence.Logs[0].EvidencePath)) -and
            (Get-FileHash `
                -LiteralPath $multipleLogEvidence.Logs[0].EvidencePath `
                -Algorithm SHA256).Hash -ceq
                $multipleLogEvidence.Logs[0].Sha256) `
        -Message "Preserved raw evidence changed after its source changed."
    [void](Remove-T11OwnedDirectory -Ownership $multipleLogOwnership)

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

    $shellActivityLogPath = Join-Path $testRoot "ShellActivityLog.xml"
    $shellArguments = Get-T11ShellStartupArguments `
        -RootSuffix "T11SHELL" `
        -ActivityLogPath $shellActivityLogPath
    $expectedShellArguments = @(
        "/RootSuffix",
        "T11SHELL",
        "/ResetSettings",
        "General",
        "/Log",
        $shellActivityLogPath,
        "/NoSplash",
        "/Command",
        "File.Exit")
    Assert-True `
        -Condition (
            $shellArguments.Count -eq $expectedShellArguments.Count -and
            ($shellArguments -join "`0") -ceq
                ($expectedShellArguments -join "`0")) `
        -Message "The combined shell invocation arguments are not exact."

    $validShellResult = [pscustomobject]@{
        AssignedBeforeResume = $true
        TimedOut = $false
        TerminationRequested = $false
        RootExitCode = 0
        JobZeroConfirmed = $true
        ProcessTreeQuiescent = $true
        CleanupFailed = $false
        StandardOutputByteLength = 0
        StandardErrorByteLength = 0
    }
    Assert-True `
        -Condition ([bool](Assert-T11ShellProcessSucceeded `
                -Result $validShellResult)) `
        -Message "The complete shell success tuple was rejected."

    $invalidShellResults = @(
        [pscustomobject]@{
            Property = "AssignedBeforeResume"
            Value = $false
            Message = "assigned before resume"
        },
        [pscustomobject]@{
            Property = "TimedOut"
            Value = $true
            Message = "timed out"
        },
        [pscustomobject]@{
            Property = "TerminationRequested"
            Value = $true
            Message = "termination"
        },
        [pscustomobject]@{
            Property = "RootExitCode"
            Value = 1460
            Message = "1460"
        },
        [pscustomobject]@{
            Property = "JobZeroConfirmed"
            Value = $false
            Message = "job-zero"
        },
        [pscustomobject]@{
            Property = "ProcessTreeQuiescent"
            Value = $false
            Message = "job-zero"
        },
        [pscustomobject]@{
            Property = "CleanupFailed"
            Value = $true
            Message = "job-zero"
        })
    foreach ($case in $invalidShellResults) {
        $invalidShellResult = $validShellResult | Select-Object *
        $invalidShellResult.($case.Property) = $case.Value
        Assert-Throws `
            -Action {
                [void](Assert-T11ShellProcessSucceeded `
                        -Result $invalidShellResult)
            } `
            -MessagePattern $case.Message
    }

    $activityLogPath = Join-Path $testRoot "ActivityLog.xml"
    [IO.File]::WriteAllBytes($activityLogPath, [byte[]]::new(0))
    Assert-Throws `
        -Action {
            [void](Get-T11ActivityLogAnalysis `
                    -ActivityLogPath $activityLogPath `
                    -ScopeTokens @("KS.RustAnalyzer") `
                    -ReportPath (Join-Path `
                        $reportRoot `
                        "activity-empty.json"))
        } `
        -MessagePattern "non-empty"
    [IO.File]::WriteAllText(
        $activityLogPath,
        "<activity><entry>",
        [Text.UTF8Encoding]::new($false))
    Assert-Throws `
        -Action {
            [void](Get-T11ActivityLogAnalysis `
                    -ActivityLogPath $activityLogPath `
                    -ScopeTokens @("KS.RustAnalyzer") `
                    -ReportPath (Join-Path `
                        $reportRoot `
                        "activity-malformed.json"))
        } `
        -MessagePattern "unexpected end|not closed"

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

    $profileTestRoot = Join-Path $testRoot "profiles"
    [void](New-Item -ItemType Directory -Path $profileTestRoot -Force)
    $validOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "valid"
    $validManifest = Write-TestInstalledManifest `
        -Ownership $validOwnership
    $validInstalledReport = Join-Path $reportRoot "installed-valid.json"
    $installed = Get-TestInstalledEvidence `
        -Ownership $validOwnership `
        -ReportPath $validInstalledReport
    $installedReport = Get-Content -LiteralPath $validInstalledReport -Raw |
        ConvertFrom-Json
    Assert-True `
        -Condition (
            $validOwnership.Reserved -and
            $validOwnership.OwnedProfilePath -ceq
                (Join-Path $validOwnership.ProfileParent `
                    "17.0_abc12345T11TEST") -and
            $installed.ManifestPath -ceq $validManifest -and
            $installed.ExtensionDirectory -ceq
                (Split-Path -Parent $validManifest) -and
            $installedReport.ExtensionDirectory -ceq
                $installed.ExtensionDirectory -and
            $installedReport.InstalledManifest.Path -ceq $validManifest -and
            $installedReport.InstalledManifest.Namespace -ceq
                "http://schemas.microsoft.com/developer/vsx-schema/2011" -and
            $installedReport.InstalledManifest.Sha256 -ceq
                (Get-FileHash `
                    -LiteralPath $validManifest `
                    -Algorithm SHA256).Hash) `
        -Message "Exact profile derivation or installed evidence was not trusted."

    $collisionRoot = Join-Path $profileTestRoot "collision\LocalAppData"
    $collisionParent = Join-Path $collisionRoot "Microsoft\VisualStudio"
    [void](New-Item -ItemType Directory -Path $collisionParent -Force)
    $exactCollision = Join-Path $collisionParent `
        "17.0_abc12345T11COLLIDE"
    [void](New-Item -ItemType Directory -Path $exactCollision)
    Assert-Throws `
        -Action {
            [void](New-T11ProfileOwnership `
                    -LocalAppData $collisionRoot `
                    -VisualStudioMajorVersion 17 `
                    -InstanceId "abc12345" `
                    -RootSuffix "T11COLLIDE")
        } `
        -MessagePattern "already exists"
    Assert-True `
        -Condition (Test-Path -LiteralPath $exactCollision) `
        -Message "A pre-existing exact profile did not survive reservation failure."

    $siblingRoot = Join-Path $profileTestRoot "sibling\LocalAppData"
    $siblingParent = Join-Path $siblingRoot "Microsoft\VisualStudio"
    [void](New-Item -ItemType Directory -Path $siblingParent -Force)
    $suffixSibling = Join-Path $siblingParent `
        "18.0_otherT11SIBLING"
    [void](New-Item -ItemType Directory -Path $suffixSibling)
    Assert-Throws `
        -Action {
            [void](New-T11ProfileOwnership `
                    -LocalAppData $siblingRoot `
                    -VisualStudioMajorVersion 17 `
                    -InstanceId "abc12345" `
                    -RootSuffix "T11SIBLING")
        } `
        -MessagePattern "already exists"
    Assert-True `
        -Condition (Test-Path -LiteralPath $suffixSibling) `
        -Message "A pre-existing suffix sibling did not survive reservation failure."

    $partialOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "partial" `
        -RootSuffix "T11PARTIAL"
    [void](New-Item `
        -ItemType Directory `
        -Path $partialOwnership.OwnedProfilePath)
    [IO.File]::WriteAllBytes(
        (Join-Path $partialOwnership.OwnedProfilePath "partial.bin"),
        [byte[]](1))
    $partialRemoved = Remove-T11OwnedProfile -Ownership $partialOwnership
    Assert-True `
        -Condition (
            $partialRemoved -and
            $partialOwnership.Removed -and
            -not (Test-Path `
                -LiteralPath $partialOwnership.OwnedProfilePath)) `
        -Message "A pre-reserved partial profile was not cleaned exactly."

    $leafOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "leaf" `
        -RootSuffix "T11LEAF"
    [IO.File]::WriteAllBytes(
        $leafOwnership.OwnedProfilePath,
        [byte[]](1, 2, 3))
    [void](Remove-T11OwnedProfile -Ownership $leafOwnership)
    Assert-True `
        -Condition (-not (Test-Path `
            -LiteralPath $leafOwnership.OwnedProfilePath)) `
        -Message "Profile cleanup did not prove leaf/file absence."

    $unownedOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "unowned" `
        -RootSuffix "T11OWNED"
    [void](New-Item `
        -ItemType Directory `
        -Path $unownedOwnership.OwnedProfilePath)
    $unownedProfile = Join-Path `
        $unownedOwnership.ProfileParent `
        "18.0_unownedT11OTHER"
    [void](New-Item -ItemType Directory -Path $unownedProfile)
    [void](Remove-T11OwnedProfile -Ownership $unownedOwnership)
    Assert-True `
        -Condition (
            -not (Test-Path `
                -LiteralPath $unownedOwnership.OwnedProfilePath) -and
            (Test-Path -LiteralPath $unownedProfile -PathType Container)) `
        -Message "Profile cleanup removed an unowned sibling."

    $invalidInstalledCases = @(
        [pscustomobject]@{
            Name = "zero-directories"
            Setup = {
                param ($ownership)
                [void](New-Item `
                    -ItemType Directory `
                    -Path (Join-Path `
                        $ownership.OwnedProfilePath `
                        "Extensions") `
                    -Force)
            }
            Message = "not found"
        },
        [pscustomobject]@{
            Name = "multiple-directories"
            Setup = {
                param ($ownership)
                foreach ($name in @("one", "two")) {
                    [void](New-Item `
                        -ItemType Directory `
                        -Path (Join-Path `
                            $ownership.OwnedProfilePath `
                            "Extensions\$name") `
                        -Force)
                }
            }
            Message = "exactly one immediate extension directory"
        },
        [pscustomobject]@{
            Name = "zero-manifests"
            Setup = {
                param ($ownership)
                [void](New-Item `
                    -ItemType Directory `
                    -Path (Join-Path `
                        $ownership.OwnedProfilePath `
                        "Extensions\publisher") `
                    -Force)
            }
            Message = "not found"
        },
        [pscustomobject]@{
            Name = "multiple-manifests"
            Setup = {
                param ($ownership)
                $manifest = Write-TestInstalledManifest `
                    -Ownership $ownership
                [IO.File]::WriteAllText(
                    (Join-Path `
                        (Split-Path -Parent $manifest) `
                        "other.vsixmanifest"),
                    "<PackageManifest />")
            }
            Message = "exactly one direct regular"
        },
        [pscustomobject]@{
            Name = "non-direct-manifest"
            Setup = {
                param ($ownership)
                $nested = Join-Path `
                    $ownership.OwnedProfilePath `
                    "Extensions\publisher\nested"
                [void](New-Item -ItemType Directory -Path $nested -Force)
                [IO.File]::WriteAllText(
                    (Join-Path $nested "extension.vsixmanifest"),
                    "<PackageManifest />")
            }
            Message = "not found"
        },
        [pscustomobject]@{
            Name = "profile-root-manifest"
            Setup = {
                param ($ownership)
                [void](New-Item `
                        -ItemType Directory `
                        -Path $ownership.OwnedProfilePath)
                [IO.File]::WriteAllText(
                    (Join-Path `
                        $ownership.OwnedProfilePath `
                        "extension.vsixmanifest"),
                    "<PackageManifest />")
            }
            Message = "not found"
        },
        [pscustomobject]@{
            Name = "extensions-root-manifest"
            Setup = {
                param ($ownership)
                $extensions = Join-Path `
                    $ownership.OwnedProfilePath `
                    "Extensions"
                [void](New-Item `
                        -ItemType Directory `
                        -Path $extensions `
                        -Force)
                [IO.File]::WriteAllText(
                    (Join-Path $extensions "extension.vsixmanifest"),
                    "<PackageManifest />")
            }
            Message = "not found"
        },
        [pscustomobject]@{
            Name = "non-extensions-manifest"
            Setup = {
                param ($ownership)
                $directory = Join-Path `
                    $ownership.OwnedProfilePath `
                    "Other\publisher"
                [void](New-Item `
                        -ItemType Directory `
                        -Path $directory `
                        -Force)
                [IO.File]::WriteAllText(
                    (Join-Path $directory "extension.vsixmanifest"),
                    "<PackageManifest />")
            }
            Message = "not found"
        },
        [pscustomobject]@{
            Name = "malformed"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content '<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">')
            }
            Message = "unexpected end|not closed"
        },
        [pscustomobject]@{
            Name = "oversized"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content ("x" * 256))
            }
            Message = "invalid byte length"
            MaximumByteLength = 64
        },
        [pscustomobject]@{
            Name = "dtd-external"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content @"
<!DOCTYPE PackageManifest [<!ENTITY external SYSTEM "file:///C:/Windows/win.ini">]>
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="&external;" Version="1.2.3" /></Metadata></PackageManifest>
"@)
            }
            Message = "DTD"
        },
        [pscustomobject]@{
            Name = "namespace-less"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content '<PackageManifest><Metadata><Identity Id="test.extension" Version="1.2.3" /></Metadata></PackageManifest>')
            }
            Message = "exact VSIX 2011 namespace"
        },
        [pscustomobject]@{
            Name = "wrong-namespace"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content '<PackageManifest xmlns="urn:wrong"><Metadata><Identity Id="test.extension" Version="1.2.3" /></Metadata></PackageManifest>')
            }
            Message = "exact VSIX 2011 namespace"
        },
        [pscustomobject]@{
            Name = "mixed-metadata-namespace"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content '<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata xmlns=""><Identity Id="test.extension" Version="1.2.3" /></Metadata></PackageManifest>')
            }
            Message = "exact VSIX 2011 namespace"
        },
        [pscustomobject]@{
            Name = "mixed-identity-namespace"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content '<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity xmlns="" Id="test.extension" Version="1.2.3" /></Metadata></PackageManifest>')
            }
            Message = "exact VSIX 2011 namespace"
        },
        [pscustomobject]@{
            Name = "duplicate-metadata"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content @"
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="test.extension" Version="1.2.3" /></Metadata><Metadata /></PackageManifest>
"@)
            }
            Message = "exactly one Metadata"
        },
        [pscustomobject]@{
            Name = "duplicate-identity"
            Setup = {
                param ($ownership)
                [void](Write-TestInstalledManifest `
                        -Ownership $ownership `
                        -Content @"
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="test.extension" Version="1.2.3" /><Identity Id="test.extension" Version="1.2.3" /></Metadata></PackageManifest>
"@)
            }
            Message = "exactly one Identity"
        })
    foreach ($case in $invalidInstalledCases) {
        $ownership = New-TestProfile `
            -Root $profileTestRoot `
            -Name "invalid-$($case.Name)" `
            -RootSuffix "T11INVALID"
        & $case.Setup $ownership
        $maximumByteLength = if ($case.PSObject.Properties.Name -contains
            "MaximumByteLength") {
            $case.MaximumByteLength
        }
        else {
            1048576
        }
        Assert-Throws `
            -Action {
                [void](Get-TestInstalledEvidence `
                        -Ownership $ownership `
                        -ReportPath (Join-Path `
                            $reportRoot `
                            "installed-$($case.Name).json") `
                        -MaximumManifestByteLength $maximumByteLength)
            } `
            -MessagePattern $case.Message
        [void](Remove-T11OwnedProfile -Ownership $ownership)
    }

    $identityCases = @(
        [pscustomobject]@{
            Name = "wrong-id"
            Id = "other.extension"
            Version = "1.2.3"
            ExpectedId = "test.extension"
            ExpectedVersion = "1.2.3"
        },
        [pscustomobject]@{
            Name = "id-case"
            Id = "Test.Extension"
            Version = "1.2.3"
            ExpectedId = "test.extension"
            ExpectedVersion = "1.2.3"
        },
        [pscustomobject]@{
            Name = "wrong-version"
            Id = "test.extension"
            Version = "9.9.9"
            ExpectedId = "test.extension"
            ExpectedVersion = "1.2.3"
        },
        [pscustomobject]@{
            Name = "version-case"
            Id = "test.extension"
            Version = "1.2.3-Preview"
            ExpectedId = "test.extension"
            ExpectedVersion = "1.2.3-preview"
        })
    foreach ($case in $identityCases) {
        $ownership = New-TestProfile `
            -Root $profileTestRoot `
            -Name "identity-$($case.Name)" `
            -RootSuffix "T11IDENTITY"
        [void](Write-TestInstalledManifest `
                -Ownership $ownership `
                -Content @"
<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="$($case.Id)" Version="$($case.Version)" /></Metadata></PackageManifest>
"@)
        Assert-Throws `
            -Action {
                [void](Get-TestInstalledEvidence `
                        -Ownership $ownership `
                        -ReportPath (Join-Path `
                            $reportRoot `
                            "identity-$($case.Name).json") `
                        -ExtensionId $case.ExpectedId `
                        -ExtensionVersion $case.ExpectedVersion)
            } `
            -MessagePattern "does not match"
        [void](Remove-T11OwnedProfile -Ownership $ownership)
    }

    $lexicalOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "lexical" `
        -RootSuffix "T11LEXICAL"
    $trustedProfilePath = $lexicalOwnership.OwnedProfilePath
    $lexicalPaths = @(
        $lexicalOwnership.LocalAppData,
        $lexicalOwnership.ProfileParent,
        "$trustedProfilePath\Extensions",
        "$trustedProfilePath\Other",
        "$trustedProfilePath\Extensions\publisher",
        "$trustedProfilePath-evil",
        "$($lexicalOwnership.ProfileParent)\.\$($lexicalOwnership.ProfileName)",
        "$($lexicalOwnership.ProfileParent)\other\..\$($lexicalOwnership.ProfileName)",
        "$($lexicalOwnership.ProfileParent)\\$($lexicalOwnership.ProfileName)",
        $trustedProfilePath.Replace("\", "/"),
        "$trustedProfilePath.",
        "$trustedProfilePath ")
    foreach ($path in $lexicalPaths) {
        $lexicalOwnership.OwnedProfilePath = $path
        Assert-Throws `
            -Action {
                [void](Get-TestInstalledEvidence `
                        -Ownership $lexicalOwnership `
                        -ReportPath (Join-Path `
                            $reportRoot `
                            "lexical-$([guid]::NewGuid()).json"))
            } `
            -MessagePattern "canonical|changed"
    }
    $lexicalOwnership.OwnedProfilePath = $trustedProfilePath
    [void](Remove-T11OwnedProfile -Ownership $lexicalOwnership)

    $localAppDataWithSlash = (Join-Path `
        $profileTestRoot `
        "alternate-root").Replace("\", "/")
    Assert-Throws `
        -Action {
            [void](New-T11ProfileOwnership `
                    -LocalAppData $localAppDataWithSlash `
                    -VisualStudioMajorVersion 17 `
                    -InstanceId "abc12345" `
                    -RootSuffix "T11ALTERNATE")
        } `
        -MessagePattern "canonical Windows path"
    Assert-Throws `
        -Action {
            [void](New-T11ProfileOwnership `
                    -LocalAppData $profileTestRoot `
                    -VisualStudioMajorVersion 17 `
                    -InstanceId "..\\other" `
                    -RootSuffix "T11INSTANCE")
        } `
        -MessagePattern "path-safe"

    $reparseAnchorTarget = Join-Path $profileTestRoot "anchor-target"
    $reparseAnchor = Join-Path $profileTestRoot "anchor-link"
    [void](New-TestJunction `
            -Path $reparseAnchor `
            -Target $reparseAnchorTarget)
    Assert-Throws `
        -Action {
            [void](New-T11ProfileOwnership `
                    -LocalAppData $reparseAnchor `
                    -VisualStudioMajorVersion 17 `
                    -InstanceId "abc12345" `
                    -RootSuffix "T11REPARSE")
        } `
        -MessagePattern "anchor.*reparse"
    Remove-Item -LiteralPath $reparseAnchor -Force

    foreach ($level in @("Microsoft", "VisualStudio")) {
        $root = Join-Path $profileTestRoot "reparse-$level"
        $localAppData = Join-Path $root "LocalAppData"
        [void](New-Item -ItemType Directory -Path $localAppData -Force)
        $target = Join-Path $root "target"
        if ($level -eq "Microsoft") {
            $link = Join-Path $localAppData "Microsoft"
        }
        else {
            [void](New-Item `
                -ItemType Directory `
                -Path (Join-Path $localAppData "Microsoft") `
                -Force)
            $link = Join-Path $localAppData "Microsoft\VisualStudio"
        }
        [void](New-TestJunction -Path $link -Target $target)
        Assert-Throws `
            -Action {
                [void](New-T11ProfileOwnership `
                        -LocalAppData $localAppData `
                        -VisualStudioMajorVersion 17 `
                        -InstanceId "abc12345" `
                        -RootSuffix "T11REPARSE")
            } `
            -MessagePattern "reparse point"
        Remove-Item -LiteralPath $link -Force
    }

    $profileLinkOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "reparse-profile" `
        -RootSuffix "T11LINK"
    $profileLinkTarget = Join-Path $profileTestRoot "profile-link-target"
    [void](New-TestJunction `
            -Path $profileLinkOwnership.OwnedProfilePath `
            -Target $profileLinkTarget)
    Assert-Throws `
        -Action {
            [void](Get-TestInstalledEvidence `
                    -Ownership $profileLinkOwnership `
                    -ReportPath (Join-Path $reportRoot "reparse-profile.json"))
        } `
        -MessagePattern "reparse point"
    Remove-Item -LiteralPath $profileLinkOwnership.OwnedProfilePath -Force
    [void](Remove-T11OwnedProfile -Ownership $profileLinkOwnership)

    $extensionsLinkOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "reparse-extensions" `
        -RootSuffix "T11LINK"
    [void](New-Item `
        -ItemType Directory `
        -Path $extensionsLinkOwnership.OwnedProfilePath)
    $extensionsLink = Join-Path `
        $extensionsLinkOwnership.OwnedProfilePath `
        "Extensions"
    [void](New-TestJunction `
            -Path $extensionsLink `
            -Target (Join-Path $profileTestRoot "extensions-link-target"))
    Assert-Throws `
        -Action {
            [void](Get-TestInstalledEvidence `
                    -Ownership $extensionsLinkOwnership `
                    -ReportPath (Join-Path $reportRoot "reparse-extensions.json"))
        } `
        -MessagePattern "reparse point"
    Remove-Item -LiteralPath $extensionsLink -Force
    [void](Remove-T11OwnedProfile -Ownership $extensionsLinkOwnership)

    $directoryLinkOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "reparse-directory" `
        -RootSuffix "T11LINK"
    $directoryExtensions = Join-Path `
        $directoryLinkOwnership.OwnedProfilePath `
        "Extensions"
    [void](New-Item -ItemType Directory -Path $directoryExtensions -Force)
    $directoryLink = Join-Path $directoryExtensions "publisher"
    [void](New-TestJunction `
            -Path $directoryLink `
            -Target (Join-Path $profileTestRoot "directory-link-target"))
    Assert-Throws `
        -Action {
            [void](Get-TestInstalledEvidence `
                    -Ownership $directoryLinkOwnership `
                    -ReportPath (Join-Path $reportRoot "reparse-directory.json"))
        } `
        -MessagePattern "reparse point"
    Remove-Item -LiteralPath $directoryLink -Force
    [void](Remove-T11OwnedProfile -Ownership $directoryLinkOwnership)

    $manifestLinkOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "reparse-manifest" `
        -RootSuffix "T11LINK"
    $manifestLinkDirectory = Join-Path `
        $manifestLinkOwnership.OwnedProfilePath `
        "Extensions\publisher"
    [void](New-Item -ItemType Directory -Path $manifestLinkDirectory -Force)
    $manifestLink = Join-Path `
        $manifestLinkDirectory `
        "extension.vsixmanifest"
    [void](New-TestJunction `
            -Path $manifestLink `
            -Target (Join-Path $profileTestRoot "manifest-link-target"))
    Assert-Throws `
        -Action {
            [void](Get-TestInstalledEvidence `
                    -Ownership $manifestLinkOwnership `
                    -ReportPath (Join-Path $reportRoot "reparse-manifest.json"))
        } `
        -MessagePattern "regular extension.vsixmanifest"
    Remove-Item -LiteralPath $manifestLink -Force
    [void](Remove-T11OwnedProfile -Ownership $manifestLinkOwnership)

    $cleanupLinkOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "cleanup-reparse" `
        -RootSuffix "T11CLEANUP"
    $cleanupLinkContainer = Join-Path `
        $cleanupLinkOwnership.OwnedProfilePath `
        "partial"
    [void](New-Item -ItemType Directory -Path $cleanupLinkContainer -Force)
    $cleanupLink = Join-Path $cleanupLinkContainer "linked"
    [void](New-TestJunction `
            -Path $cleanupLink `
            -Target (Join-Path $profileTestRoot "cleanup-link-target"))
    Assert-Throws `
        -Action {
            [void](Remove-T11OwnedProfile `
                    -Ownership $cleanupLinkOwnership)
        } `
        -MessagePattern "reparse point"
    Assert-True `
        -Condition (
            (Test-Path `
                -LiteralPath $cleanupLinkOwnership.OwnedProfilePath) -and
            (Test-Path -LiteralPath $cleanupLink)) `
        -Message "Unsafe profile cleanup deleted before rejecting a reparse point."
    Remove-Item -LiteralPath $cleanupLink -Force
    [void](Remove-T11OwnedProfile -Ownership $cleanupLinkOwnership)

    $suffixSiblingCleanupOwnership = New-TestProfile `
        -Root $profileTestRoot `
        -Name "cleanup-suffix-sibling" `
        -RootSuffix "T11AMBIGUOUS"
    [void](New-Item `
        -ItemType Directory `
        -Path $suffixSiblingCleanupOwnership.OwnedProfilePath)
    $sameSuffixSibling = Join-Path `
        $suffixSiblingCleanupOwnership.ProfileParent `
        "18.0_otherT11AMBIGUOUS"
    [void](New-Item -ItemType Directory -Path $sameSuffixSibling)
    [void](Remove-T11OwnedProfile `
            -Ownership $suffixSiblingCleanupOwnership)
    Assert-True `
        -Condition (
            -not (Test-Path `
                -LiteralPath $suffixSiblingCleanupOwnership.OwnedProfilePath) -and
            (Test-Path -LiteralPath $sameSuffixSibling)) `
        -Message "Exact profile cleanup used a suffix search or deleted an unowned sibling."
    Remove-Item -LiteralPath $sameSuffixSibling -Recurse -Force

    $ownedDirectoryAnchor = Join-Path $testRoot "owned-directory"
    [void](New-Item -ItemType Directory -Path $ownedDirectoryAnchor -Force)
    $preExistingDirectory = Join-Path $ownedDirectoryAnchor "preexisting"
    [void](New-Item -ItemType Directory -Path $preExistingDirectory)
    Assert-Throws `
        -Action {
            [void](New-T11OwnedDirectory `
                    -AnchorPath $ownedDirectoryAnchor `
                    -Path $preExistingDirectory)
        } `
        -MessagePattern "already exists"
    Assert-True `
        -Condition (Test-Path -LiteralPath $preExistingDirectory) `
        -Message "A pre-existing generated directory was deleted."

    $reservedDirectory = New-T11OwnedDirectory `
        -AnchorPath $ownedDirectoryAnchor `
        -Path (Join-Path $ownedDirectoryAnchor "reserved-partial")
    [IO.File]::WriteAllBytes($reservedDirectory.Path, [byte[]](1))
    [void](Remove-T11OwnedDirectory -Ownership $reservedDirectory)
    Assert-True `
        -Condition (-not (Test-Path -LiteralPath $reservedDirectory.Path)) `
        -Message "A partially created reserved path was not cleaned exactly."

    $ownedDirectory = New-T11OwnedDirectory `
        -AnchorPath $ownedDirectoryAnchor `
        -Path (Join-Path $ownedDirectoryAnchor "run-owned")
    [void](Initialize-T11OwnedDirectory -Ownership $ownedDirectory)
    $ownedDirectoryLink = Join-Path $ownedDirectory.Path "linked"
    [void](New-TestJunction `
            -Path $ownedDirectoryLink `
            -Target (Join-Path $ownedDirectoryAnchor "link-target"))
    Assert-Throws `
        -Action {
            [void](Remove-T11OwnedDirectory -Ownership $ownedDirectory)
        } `
        -MessagePattern "reparse point"
    Assert-True `
        -Condition (Test-Path -LiteralPath $ownedDirectory.Path) `
        -Message "Unsafe generated-directory cleanup deleted before validation."
    Remove-Item -LiteralPath $ownedDirectoryLink -Force
    [void](Remove-T11OwnedDirectory -Ownership $ownedDirectory)
    [void](Remove-T11OwnedProfile -Ownership $validOwnership)

    $pwshPath = [Environment]::ProcessPath
    $childEnvironmentOwnership = New-TestOwnedDirectory `
        -Root $testRoot `
        -Name "child environment"
    $argumentScript = Join-Path $testRoot "argument-evidence.ps1"
    Write-TestScript `
        -Path $argumentScript `
        -Content @'
$evidence = [ordered]@{
    Values = @($args)
    CurrentDirectory = [Environment]::CurrentDirectory
    Location = (Get-Location).Path
    Temp = $env:TEMP
    Tmp = $env:TMP
}
[Console]::Out.Write(($evidence | ConvertTo-Json -Compress))
'@
    $parentTemp = $env:TEMP
    $parentTmp = $env:TMP
    $childEnvironment = [ordered]@{
        TEMP = $childEnvironmentOwnership.Path
        TMP = $childEnvironmentOwnership.Path
    }
    $argumentValues = @(
        "plain",
        "with space",
        'quote"inside',
        'slashes\\"quote',
        "trailing\",
        "space trail\",
        "",
        "two\\slashes")
    $childEnvironmentOutput = Join-Path `
        $reportRoot `
        "process-environment.stdout.log"
    $environmentResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList (@(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            $argumentScript) + $argumentValues) `
        -StandardOutputPath $childEnvironmentOutput `
        -StandardErrorPath (Join-Path `
            $reportRoot `
            "process-environment.stderr.log") `
        -TimeoutSeconds 10 `
        -WorkingDirectory $childEnvironmentOwnership.Path `
        -EnvironmentVariables $childEnvironment
    $argumentEvidence = Get-Content `
        -LiteralPath $childEnvironmentOutput `
        -Raw |
        ConvertFrom-Json
    Assert-True `
        -Condition (
            $environmentResult.ExitCode -eq 0 -and
            $environmentResult.RootProcessId -gt 0 -and
            $environmentResult.RootExitCode -eq 0 -and
            $environmentResult.AssignedBeforeResume -and
            $environmentResult.JobZeroConfirmed -and
            $environmentResult.ProcessTreeQuiescent -and
            -not $environmentResult.TimedOut -and
            -not $environmentResult.TerminationRequested -and
            -not $environmentResult.CleanupFailed -and
            $environmentResult.StandardOutputByteLength -gt 0 -and
            $environmentResult.StandardErrorByteLength -eq 0 -and
            @($argumentEvidence.Values).Count -eq $argumentValues.Count -and
            (@($argumentEvidence.Values) -join "`0") -ceq
                ($argumentValues -join "`0") -and
            $argumentEvidence.CurrentDirectory -ceq
                $childEnvironmentOwnership.Path -and
            $argumentEvidence.Location -ceq
                $childEnvironmentOwnership.Path -and
            $argumentEvidence.Temp -ceq $childEnvironmentOwnership.Path -and
            $argumentEvidence.Tmp -ceq $childEnvironmentOwnership.Path -and
            $env:TEMP -ceq $parentTemp -and
            $env:TMP -ceq $parentTmp -and
            $environmentResult.PSObject.Properties.Name -notcontains
                "EnvironmentVariables") `
        -Message "Arguments, working directory, or child-only environment were not exact."
    [void](Remove-T11OwnedDirectory `
            -Ownership $childEnvironmentOwnership)

    $nonzeroResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @("-NoProfile", "-Command", "exit 37") `
        -StandardOutputPath (Join-Path $reportRoot "process-nonzero.stdout.log") `
        -StandardErrorPath (Join-Path $reportRoot "process-nonzero.stderr.log") `
        -TimeoutSeconds 10
    Assert-True `
        -Condition (
            -not $nonzeroResult.TimedOut -and
            $nonzeroResult.AssignedBeforeResume -and
            $nonzeroResult.JobZeroConfirmed -and
            $nonzeroResult.ExitCode -eq 37) `
        -Message "The retained root-process handle did not report its exact nonzero exit."

    $grandchildSentinel = Join-Path $testRoot "grandchild-completed.txt"
    $grandchildScript = Join-Path $testRoot "grandchild.ps1"
    $intermediateScript = Join-Path $testRoot "intermediate.ps1"
    $rootScript = Join-Path $testRoot "root.ps1"
    Write-TestScript `
        -Path $grandchildScript `
        -Content @"
Start-Sleep -Milliseconds 700
[IO.File]::WriteAllText('$($grandchildSentinel.Replace("'", "''"))', "grandchild")
"@
    Write-TestScript `
        -Path $intermediateScript `
        -Content @"
Start-Sleep -Milliseconds 400
[void](Start-Process -FilePath '$($pwshPath.Replace("'", "''"))' -ArgumentList @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", '$($grandchildScript.Replace("'", "''"))') -WindowStyle Hidden)
"@
    Write-TestScript `
        -Path $rootScript `
        -Content @"
[void](Start-Process -FilePath '$($pwshPath.Replace("'", "''"))' -ArgumentList @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", '$($intermediateScript.Replace("'", "''"))') -WindowStyle Hidden)
"@
    $processTreeStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $processTreeResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            $rootScript) `
        -StandardOutputPath (Join-Path $reportRoot "process-tree.stdout.log") `
        -StandardErrorPath (Join-Path $reportRoot "process-tree.stderr.log") `
        -TimeoutSeconds 10
    $processTreeStopwatch.Stop()
    Assert-True `
        -Condition (
            -not $processTreeResult.TimedOut -and
            $processTreeResult.AssignedBeforeResume -and
            $processTreeResult.JobZeroConfirmed -and
            $processTreeResult.ExitCode -eq 0 -and
            $processTreeResult.ProcessTreeQuiescent -and
            (Test-Path -LiteralPath $grandchildSentinel -PathType Leaf) -and
            $processTreeStopwatch.Elapsed.TotalSeconds -ge 1) `
        -Message "Late-grandchild evidence failed: timedOut=$($processTreeResult.TimedOut), assigned=$($processTreeResult.AssignedBeforeResume), zero=$($processTreeResult.JobZeroConfirmed), exit=$($processTreeResult.ExitCode), sentinel=$(Test-Path -LiteralPath $grandchildSentinel), elapsed=$($processTreeStopwatch.Elapsed.TotalMilliseconds)ms."

    $rawOutputScript = Join-Path $testRoot "raw-output.ps1"
    $rawOutputRootScript = Join-Path $testRoot "raw-output-root.ps1"
    $expectedStdout = [byte[]](0x00, 0x41, 0xFF, 0x0A)
    $expectedStderr = [byte[]](0xFE, 0x42, 0x00, 0x0D)
    Write-TestScript `
        -Path $rawOutputScript `
        -Content @'
Start-Sleep -Milliseconds 700
$stdout = [Console]::OpenStandardOutput()
$stderr = [Console]::OpenStandardError()
$stdoutBytes = [byte[]](0x00, 0x41, 0xFF, 0x0A)
$stderrBytes = [byte[]](0xFE, 0x42, 0x00, 0x0D)
$stdout.Write($stdoutBytes, 0, $stdoutBytes.Length)
$stdout.Flush()
$stderr.Write($stderrBytes, 0, $stderrBytes.Length)
$stderr.Flush()
'@
    Write-TestScript `
        -Path $rawOutputRootScript `
        -Content @'
param($Pwsh, $Child)
[void](Start-Process -FilePath $Pwsh -ArgumentList @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $Child) -NoNewWindow)
'@
    $rawStdout = Join-Path $reportRoot "raw-descendant.stdout.log"
    $rawStderr = Join-Path $reportRoot "raw-descendant.stderr.log"
    $rawResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            $rawOutputRootScript,
            $pwshPath,
            $rawOutputScript) `
        -StandardOutputPath $rawStdout `
        -StandardErrorPath $rawStderr `
        -TimeoutSeconds 10
    Assert-True `
        -Condition (
            $rawResult.JobZeroConfirmed -and
            $rawResult.ProcessTreeQuiescent -and
            [Convert]::ToHexString(
                [IO.File]::ReadAllBytes($rawStdout)) -ceq
                [Convert]::ToHexString($expectedStdout) -and
            [Convert]::ToHexString(
                [IO.File]::ReadAllBytes($rawStderr)) -ceq
                [Convert]::ToHexString($expectedStderr)) `
        -Message "Descendant-held stdout/stderr handles did not produce exact raw bytes."

    $timeoutStarted = Join-Path $testRoot "timeout-grandchild-started.txt"
    $timeoutSentinel = Join-Path $testRoot "timeout-grandchild-survived.txt"
    $timeoutGrandchild = Join-Path $testRoot "timeout-grandchild.ps1"
    $timeoutIntermediate = Join-Path $testRoot "timeout-intermediate.ps1"
    $timeoutRoot = Join-Path $testRoot "timeout-root.ps1"
    Write-TestScript `
        -Path $timeoutGrandchild `
        -Content @"
[IO.File]::WriteAllText('$($timeoutStarted.Replace("'", "''"))', "started")
Start-Sleep -Seconds 30
[IO.File]::WriteAllText('$($timeoutSentinel.Replace("'", "''"))', "survived")
"@
    Write-TestScript `
        -Path $timeoutIntermediate `
        -Content @"
[void](Start-Process -FilePath '$($pwshPath.Replace("'", "''"))' -ArgumentList @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", '$($timeoutGrandchild.Replace("'", "''"))') -WindowStyle Hidden)
Start-Sleep -Seconds 30
"@
    Write-TestScript `
        -Path $timeoutRoot `
        -Content @"
[void](Start-Process -FilePath '$($pwshPath.Replace("'", "''"))' -ArgumentList @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", '$($timeoutIntermediate.Replace("'", "''"))') -WindowStyle Hidden)
Start-Sleep -Seconds 30
"@
    $timeoutStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $timeoutResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            $timeoutRoot) `
        -StandardOutputPath (Join-Path $reportRoot "process-timeout.stdout.log") `
        -StandardErrorPath (Join-Path $reportRoot "process-timeout.stderr.log") `
        -TimeoutSeconds 5
    $timeoutStopwatch.Stop()
    Assert-True `
        -Condition (
            $timeoutResult.TimedOut -and
            $timeoutResult.TerminationRequested -and
            $timeoutResult.JobZeroConfirmed -and
            $timeoutResult.ProcessTreeQuiescent -and
            -not $timeoutResult.CleanupFailed -and
            (Test-Path -LiteralPath $timeoutStarted -PathType Leaf) -and
            -not (Test-Path -LiteralPath $timeoutSentinel) -and
            $timeoutResult.ElapsedMilliseconds -le 5250 -and
            $timeoutStopwatch.Elapsed.TotalMilliseconds -le 5500) `
        -Message "Timeout evidence failed: timedOut=$($timeoutResult.TimedOut), terminate=$($timeoutResult.TerminationRequested), zero=$($timeoutResult.JobZeroConfirmed), quiescent=$($timeoutResult.ProcessTreeQuiescent), cleanupFailed=$($timeoutResult.CleanupFailed), started=$(Test-Path -LiteralPath $timeoutStarted), survived=$(Test-Path -LiteralPath $timeoutSentinel), native=$($timeoutResult.ElapsedMilliseconds)ms, wall=$($timeoutStopwatch.Elapsed.TotalMilliseconds)ms."
    Start-Sleep -Milliseconds 500
    Assert-True `
        -Condition (-not (Test-Path -LiteralPath $timeoutSentinel)) `
        -Message "A timed-out job descendant survived termination."

    $handleProcess = [Diagnostics.Process]::GetCurrentProcess()
    try {
        $failureHandleBaseline = $handleProcess.HandleCount
    }
    finally {
        $handleProcess.Dispose()
    }
    foreach ($failurePoint in @("Create", "Assign", "Resume")) {
        $failureSentinel = Join-Path `
            $testRoot `
            "failure-$failurePoint-sentinel.txt"
        $failureStdout = Join-Path `
            $reportRoot `
            "failure-$failurePoint.stdout.log"
        $failureStderr = Join-Path `
            $reportRoot `
            "failure-$failurePoint.stderr.log"
        Assert-Throws `
            -Action {
                [void](Invoke-T11BoundedProcess `
                        -FilePath $pwshPath `
                        -ArgumentList @(
                            "-NoProfile",
                            "-Command",
                            "[IO.File]::WriteAllText('$failureSentinel', 'ran')") `
                        -StandardOutputPath $failureStdout `
                        -StandardErrorPath $failureStderr `
                        -TimeoutSeconds 10 `
                        -TestFailurePoint $failurePoint)
            } `
            -MessagePattern "Synthetic .* failure"
        Start-Sleep -Milliseconds 100
        Remove-Item -LiteralPath $failureStdout -Force
        Remove-Item -LiteralPath $failureStderr -Force
        Assert-True `
            -Condition (
                -not (Test-Path -LiteralPath $failureSentinel) -and
                -not (Test-Path -LiteralPath $failureStdout) -and
                -not (Test-Path -LiteralPath $failureStderr)) `
            -Message "$failurePoint failure leaked a process or native output handle."
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    $handleProcess = [Diagnostics.Process]::GetCurrentProcess()
    try {
        $failureHandleDelta =
            $handleProcess.HandleCount - $failureHandleBaseline
    }
    finally {
        $handleProcess.Dispose()
    }
    $failureHandleIncrease = [Math]::Max(0, $failureHandleDelta)
    Assert-True `
        -Condition ($failureHandleIncrease -le 2) `
        -Message "Create/assign/resume failures leaked native handles."

    $controllerStarted = Join-Path $testRoot "controller-child-started.txt"
    $controllerSentinel = Join-Path $testRoot "controller-child-survived.txt"
    $controllerChild = Join-Path $testRoot "controller-child.ps1"
    $controllerScript = Join-Path $testRoot "controller.ps1"
    $controllerInnerStdout = Join-Path `
        $reportRoot `
        "controller-inner.stdout.log"
    $controllerInnerStderr = Join-Path `
        $reportRoot `
        "controller-inner.stderr.log"
    Write-TestScript `
        -Path $controllerChild `
        -Content @'
param($Started, $Sentinel)
[IO.File]::WriteAllText($Started, "started")
Start-Sleep -Seconds 5
[IO.File]::WriteAllText($Sentinel, "survived")
'@
    Write-TestScript `
        -Path $controllerScript `
        -Content @"
`$ErrorActionPreference = "Stop"
Import-Module '$($PSScriptRoot.Replace("'", "''"))\T11Validation.psm1' -Force
[RustAnalyzerVs.T11Private.JobProcess]::TerminateCurrentProcessAfterDelayForTest(2000, 197)
[void](Invoke-T11BoundedProcess -FilePath '$($pwshPath.Replace("'", "''"))' -ArgumentList @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", '$($controllerChild.Replace("'", "''"))', '$($controllerStarted.Replace("'", "''"))', '$($controllerSentinel.Replace("'", "''"))') -StandardOutputPath '$($controllerInnerStdout.Replace("'", "''"))' -StandardErrorPath '$($controllerInnerStderr.Replace("'", "''"))' -TimeoutSeconds 20)
"@
    $controllerResult = Invoke-T11BoundedProcess `
        -FilePath $pwshPath `
        -ArgumentList @(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            $controllerScript) `
        -StandardOutputPath (Join-Path `
            $reportRoot `
            "controller-outer.stdout.log") `
        -StandardErrorPath (Join-Path `
            $reportRoot `
            "controller-outer.stderr.log") `
        -TimeoutSeconds 10
    Assert-True `
        -Condition (
            $controllerResult.JobZeroConfirmed -and
            $controllerResult.ExitCode -eq 197 -and
            (Test-Path -LiteralPath $controllerStarted -PathType Leaf)) `
        -Message "The separate controller did not die after launching its delayed descendant."
    Start-Sleep -Seconds 4
    Assert-True `
        -Condition (-not (Test-Path -LiteralPath $controllerSentinel)) `
        -Message "Kill-on-close did not terminate the dead controller's delayed descendant."

    $unsafeCleanupProfile = New-TestProfile `
        -Root $profileTestRoot `
        -Name "unsafe-lifecycle-cleanup" `
        -RootSuffix "T11UNSAFE"
    [void](New-Item `
            -ItemType Directory `
            -Path $unsafeCleanupProfile.OwnedProfilePath)
    $unsafeCleanupDirectory = New-TestOwnedDirectory `
        -Root $testRoot `
        -Name "unsafe-lifecycle-directory"
    $unsafeResult = [pscustomobject]@{
        RootProcessId = 4242
        AssignedBeforeResume = $true
        JobZeroConfirmed = $false
        ProcessTreeQuiescent = $false
        CleanupFailed = $true
    }
    Assert-Throws `
        -Action {
            [void](Assert-T11CleanupProcessSafety `
                    -RequiredInvocationCount 1 `
                    -InvocationResults @($unsafeResult))
            [void](Remove-T11OwnedProfile `
                    -Ownership $unsafeCleanupProfile)
            [void](Remove-T11OwnedDirectory `
                    -Ownership $unsafeCleanupDirectory)
        } `
        -MessagePattern "job-zero"
    Assert-True `
        -Condition (
            (Test-Path `
                -LiteralPath $unsafeCleanupProfile.OwnedProfilePath) -and
            (Test-Path -LiteralPath $unsafeCleanupDirectory.Path)) `
        -Message "Owned profile or directory deletion ignored missing job-zero evidence."
    $reusedPidResults = @(
        [pscustomobject]@{
            RootProcessId = 4242
            AssignedBeforeResume = $true
            JobZeroConfirmed = $true
            ProcessTreeQuiescent = $true
            CleanupFailed = $false
        },
        [pscustomobject]@{
            RootProcessId = 4242
            AssignedBeforeResume = $true
            JobZeroConfirmed = $true
            ProcessTreeQuiescent = $true
            CleanupFailed = $false
        })
    [void](Assert-T11CleanupProcessSafety `
            -RequiredInvocationCount 2 `
            -InvocationResults $reusedPidResults)
    [void](Remove-T11OwnedProfile -Ownership $unsafeCleanupProfile)
    [void](Remove-T11OwnedDirectory -Ownership $unsafeCleanupDirectory)
    Assert-True `
        -Condition (
            -not (Test-Path `
                -LiteralPath $unsafeCleanupProfile.OwnedProfilePath) -and
            -not (Test-Path -LiteralPath $unsafeCleanupDirectory.Path)) `
        -Message "Diagnostic PID reuse influenced exact owned-path cleanup."

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
    $startupPhase = [regex]::Match(
        $hostScript,
        '(?s)\$currentPhase = "Startup".*?(?=\$currentPhase = "ActivityLog")')
    $activityPhase = [regex]::Match(
        $hostScript,
        '(?s)\$currentPhase = "ActivityLog".*?(?=\$currentPhase = "Acceptance")')
    Assert-True `
        -Condition (
            $startupPhase.Success -and
            ([regex]::Matches(
                $startupPhase.Value,
                "Invoke-T11BoundedProcess")).Count -eq 1 -and
            $startupPhase.Value -match
                '(?s)Invoke-T11BoundedProcess\s+`\r?\n\s+-FilePath \$selectedHost\.DevenvPath' -and
            ([regex]::Matches(
                $hostScript,
                '(?s)Invoke-T11BoundedProcess\s+`\r?\n\s+-FilePath \$selectedHost\.DevenvPath')).Count -eq 1 -and
            $startupPhase.Value -match
                "Get-T11ShellStartupArguments" -and
            $startupPhase.Value -match
                "Assert-T11ShellProcessSucceeded" -and
            $startupPhase.Value -match
                'Write-Json -Path \$startupCommandPath -Value \$startupResult' -and
            $startupPhase.Value -match
                '-TimeoutSeconds \$ProcessTimeoutSeconds' -and
            $startupPhase.Value -notmatch
                "(?i)bootstrap|retry|fallback|settings collection|registry|reg\.exe|DTE|UIAutomation|Start-Process" -and
            $activityPhase.Success -and
            $activityPhase.Value -match
                '-ActivityLogPath \$activityLogPath' -and
            ([regex]::Matches(
                $hostScript,
                "Get-T11ActivityLogAnalysis")).Count -eq 1 -and
            ([regex]::Matches(
                $hostScript,
                "Get-T11InstalledExtensionEvidence")).Count -eq 1 -and
            $hostScript.IndexOf(
                "Get-T11InstalledExtensionEvidence",
                [StringComparison]::Ordinal) -lt $startupPhase.Index) `
        -Message "Shell startup is not one combined bounded invocation with one Activity Log analysis."
    $installPhase = [regex]::Match(
        $hostScript,
        '(?s)\$currentPhase = "Install".*?(?=\$currentPhase = "InstalledIdentity")')
    Assert-True `
        -Condition (
            $installPhase.Success -and
            $installPhase.Value -notmatch "/logFile" -and
            $installPhase.Value -match
                'TEMP = \$installerTempOwnership\.Path' -and
            $installPhase.Value -match
                'TMP = \$installerTempOwnership\.Path' -and
            $installPhase.Value.IndexOf(
                "Invoke-T11BoundedProcess",
                [StringComparison]::Ordinal) -lt
                $installPhase.Value.IndexOf(
                    "Save-T11InstallerLogs",
                    [StringComparison]::Ordinal) -and
            $installPhase.Value.IndexOf(
                "Save-T11InstallerLogs",
                [StringComparison]::Ordinal) -lt
                $installPhase.Value.IndexOf(
                    "Assert-ProcessSucceeded",
                    [StringComparison]::Ordinal)) `
        -Message "Installer execution does not use isolated raw-log diagnostics."
    Assert-True `
        -Condition (
            $hostScript -match
                'New-T11ProfileOwnership[\s\S]*?-LocalAppData \$env:LOCALAPPDATA' -and
            $hostScript -match
                'New-T11OwnedDirectory[\s\S]*?-Path \$installerTempDirectory' -and
            $hostScript -match
                'Initialize-T11OwnedDirectory[\s\S]*?\$installerTempOwnership' -and
            $hostScript -match
                'New-T11OwnedDirectory[\s\S]*?-Path \$installerRawLogDirectory' -and
            $hostScript -match
                'Initialize-T11OwnedDirectory[\s\S]*?\$installerRawLogOwnership' -and
            $hostScript -match
                'Save-T11InstallerLogs[\s\S]*?-RawLogOwnership \$installerRawLogOwnership' -and
            $hostScript -match
                'Get-T11InstalledExtensionEvidence[\s\S]*?-Ownership \$profileOwnership' -and
            $hostScript -match "Remove-T11OwnedProfile" -and
            $hostScript -match "Remove-T11OwnedDirectory") `
        -Message "Trusted profile or explicit directory ownership is not wired end to end."
    $profileCleanup = [regex]::Match(
        $module,
        '(?s)function Remove-T11OwnedProfile.*?(?=function Test-T11PackageLoadFault)')
    Assert-True `
        -Condition (
            $profileCleanup.Success -and
            $profileCleanup.Value -notmatch
                'Get-T11ProfileSuffixEntries|EndsWith|RootSuffix' -and
            $profileCleanup.Value -match
                'Remove-Item -LiteralPath \$paths\.ProfilePath') `
        -Message "Profile cleanup must inspect and delete only the exact reserved path."
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
                "(?i)\bStop-Process\b|Get-CimInstance|Win32_Process|Stop-T11ProcessTree|Get-T11DescendantProcessIds|ReadToEndAsync|WaitForExit|taskkill|/shutdownprocesses|VSIXInstaller\.exe\s+/\?" -and
            $hostScript -notmatch "Compress-Archive|New-TestAdapterPackage" -and
            $hostScript -notmatch "Get-IsolatedProfiles" -and
            $hostScript -match "Remove-T11OwnedProfile" -and
            $hostScript -match "/instanceIds:" -and
            $hostScript -match "/rootSuffix:" -and
            ($hostScript + $module) -match '"File\.Exit"' -and
            $hostScript -match "Not in T11 - T13" -and
            $hostScript -match "Not in T11 - T14") `
        -Message "T11 host validation violates process, targeting, or evidence-matrix scope."
    Assert-True `
        -Condition (
            $module -match "CreateJobObjectW" -and
            $module -match "JobObjectLimitKillOnJobClose" -and
            $module -match "CreateIoCompletionPort" -and
            $module -match "JobObjectMsgActiveProcessZero" -and
            $module -match "CreateProcessW" -and
            $module -match "CreateSuspended" -and
            $module -match "CreateUnicodeEnvironment" -and
            $module -match "ExtendedStartupInfoPresent" -and
            $module -match "ProcThreadAttributeHandleList" -and
            $module -match "AssignProcessToJobObject" -and
            $module -match "IsProcessInJob" -and
            $module -match "ResumeThread" -and
            $module -match "TerminateJobObject" -and
            $module -notmatch "EnvironmentVariables\s*=") `
        -Message "The private Job Object launch contract is incomplete or serializes environment state."
    Assert-True `
        -Condition (
            $hostScript -match "Assert-T11CleanupProcessSafety" -and
            $hostScript -match
                '\$cleanupDeletionAllowed = \$true' -and
            $hostScript -match
                'if \(\$cleanupDeletionAllowed\)' -and
            $hostScript -notmatch
                "Stop-RemainingOwnedProcesses|StoppedOwnedProcessIds") `
        -Message "Host cleanup is not gated exclusively by confirmed job-zero evidence."
    Assert-True `
        -Condition (
            $hostScript -match
                '(?s)if \(\$mainFailure\).*?\$failures\.Add\(\$mainFailure\.Exception\.Message\).*?foreach \(\$failure in \$cleanupFailures\)' -and
            $module -match
                'Test-Path -LiteralPath \$paths\.ProfilePath') `
        -Message "Primary failure preservation or exact leaf cleanup proof is missing."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host (
    "T11 Job Object timings: late-grandchild={0}ms; raw-output={1}ms; timeout={2}ms; controller-kill={3}ms; failure-handle-increase={4}." -f
    $processTreeResult.ElapsedMilliseconds,
    $rawResult.ElapsedMilliseconds,
    $timeoutResult.ElapsedMilliseconds,
    $controllerResult.ElapsedMilliseconds,
    $failureHandleIncrease)
Write-Host "T11 validation tests passed: $assertionCount assertions."
