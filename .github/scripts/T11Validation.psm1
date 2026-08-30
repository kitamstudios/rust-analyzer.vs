Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:T11Artifacts = @(
    [pscustomobject]@{
        Name = "MainVsix"
        RelativePath = "projects/RustAnalyzer/RustAnalyzer.vsix"
    },
    [pscustomobject]@{
        Name = "TestAdapter"
        RelativePath = "projects/RustAnalyzer.TestAdapter/KS.RustAnalyzer.TestAdapter.zip"
    })
$script:T11ManifestRelativePath = "t11/canonical-artifacts.json"

function Write-T11Json {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Value
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }

    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Get-T11ArtifactDefinitions {
    return @($script:T11Artifacts)
}

function New-T11ArtifactManifest {
    param (
        [Parameter(Mandatory)]
        [string] $ArtifactRoot,

        [Parameter(Mandatory)]
        [string] $ManifestPath
    )

    $artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
    $expectedManifestPath = [IO.Path]::GetFullPath(
        (Join-Path $artifactRoot $script:T11ManifestRelativePath))
    if ([IO.Path]::GetFullPath($ManifestPath) -ne $expectedManifestPath) {
        throw "The T11 artifact manifest must be written to '$expectedManifestPath'."
    }

    $records = foreach ($artifact in $script:T11Artifacts) {
        $path = Join-Path $artifactRoot $artifact.RelativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "The canonical T11 artifact '$path' was not found."
        }

        $file = Get-Item -LiteralPath $path
        if ($file.Length -le 0) {
            throw "The canonical T11 artifact '$path' is empty."
        }

        [ordered]@{
            Name = $artifact.Name
            RelativePath = $artifact.RelativePath
            Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            ByteLength = $file.Length
        }
    }

    $manifest = [ordered]@{
        SchemaVersion = 1
        Artifacts = @($records)
    }
    Write-T11Json -Path $expectedManifestPath -Value $manifest
    return $manifest
}

function Test-T11ArtifactTransport {
    param (
        [Parameter(Mandatory)]
        [string] $ArtifactRoot,

        [Parameter(Mandatory)]
        [string] $ManifestPath,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
    $manifestPath = [IO.Path]::GetFullPath($ManifestPath)
    $report = [ordered]@{
        Status = "Failed"
        ArtifactRoot = $artifactRoot
        ManifestPath = $manifestPath
        Artifacts = @()
        UnexpectedFiles = @()
        Error = $null
    }

    try {
        if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
            throw "The downloaded T11 artifact root '$artifactRoot' was not found."
        }

        $expectedManifestPath = [IO.Path]::GetFullPath(
            (Join-Path $artifactRoot $script:T11ManifestRelativePath))
        if ($manifestPath -ne $expectedManifestPath) {
            throw "The T11 artifact manifest must be '$expectedManifestPath'."
        }

        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "The downloaded T11 artifact manifest '$manifestPath' was not found."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.SchemaVersion -ne 1) {
            throw "The T11 artifact manifest schema version is not supported."
        }

        $records = @($manifest.Artifacts)
        if ($records.Count -ne $script:T11Artifacts.Count) {
            throw "The T11 artifact manifest must contain exactly $($script:T11Artifacts.Count) records."
        }

        $evidence = [Collections.Generic.List[object]]::new()
        foreach ($artifact in $script:T11Artifacts) {
            $record = @($records | Where-Object {
                    $_.Name -ceq $artifact.Name -and
                    $_.RelativePath -ceq $artifact.RelativePath
                })
            if ($record.Count -ne 1) {
                throw "The T11 artifact manifest must contain exactly one '$($artifact.Name)' record."
            }

            $record = $record[0]
            if ([string]$record.Sha256 -cnotmatch "^[0-9A-F]{64}$") {
                throw "The '$($artifact.Name)' SHA-256 record is invalid."
            }

            $path = Join-Path $artifactRoot $artifact.RelativePath
            $exists = Test-Path -LiteralPath $path -PathType Leaf
            $actualHash = $null
            $actualLength = $null
            if ($exists) {
                $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                $actualLength = (Get-Item -LiteralPath $path).Length
            }

            $hashMatches = $exists -and $actualHash -ceq [string]$record.Sha256
            $lengthMatches = $exists -and $actualLength -eq [long]$record.ByteLength
            $evidence.Add([ordered]@{
                    Name = $artifact.Name
                    RelativePath = $artifact.RelativePath
                    ExpectedSha256 = [string]$record.Sha256
                    ActualSha256 = $actualHash
                    ExpectedByteLength = [long]$record.ByteLength
                    ActualByteLength = $actualLength
                    HashMatches = $hashMatches
                    ByteLengthMatches = $lengthMatches
                })
        }
        $report.Artifacts = @($evidence)

        $expectedFiles = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        [void]$expectedFiles.Add($script:T11ManifestRelativePath)
        foreach ($artifact in $script:T11Artifacts) {
            [void]$expectedFiles.Add($artifact.RelativePath)
        }

        $unexpected = [Collections.Generic.List[string]]::new()
        foreach ($file in Get-ChildItem -LiteralPath $artifactRoot -File -Recurse) {
            $relativePath = [IO.Path]::GetRelativePath($artifactRoot, $file.FullName).
                Replace("\", "/")
            if (-not $expectedFiles.Contains($relativePath)) {
                $unexpected.Add($relativePath)
            }
        }
        $report.UnexpectedFiles = @($unexpected)

        $failedEvidence = @($evidence | Where-Object {
                -not $_.HashMatches -or -not $_.ByteLengthMatches
            })
        if ($failedEvidence.Count -gt 0) {
            throw "One or more downloaded T11 artifacts do not match the producer records."
        }

        if ($unexpected.Count -gt 0) {
            throw "The downloaded T11 transport contains unexpected files."
        }

        $report.Status = "Passed"
        return [pscustomobject]@{
            MainVsixPath = Join-Path $artifactRoot $script:T11Artifacts[0].RelativePath
            TestAdapterPath = Join-Path $artifactRoot $script:T11Artifacts[1].RelativePath
        }
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function Get-T11VsixIdentity {
    param (
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "VSIX '$Path' was not found."
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $manifestEntries = @($archive.Entries | Where-Object {
                $_.FullName -ceq "extension.vsixmanifest"
            })
        if ($manifestEntries.Count -ne 1) {
            throw "VSIX '$Path' must contain one root extension.vsixmanifest."
        }

        $stream = $manifestEntries[0].Open()
        $reader = [IO.StreamReader]::new($stream)
        try {
            [xml]$manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }

        $identity = $manifest.SelectSingleNode(
            "/*[local-name()='PackageManifest']/*[local-name()='Metadata']/*[local-name()='Identity']")
        $displayName = $manifest.SelectSingleNode(
            "/*[local-name()='PackageManifest']/*[local-name()='Metadata']/*[local-name()='DisplayName']")
        if (-not $identity -or
            [string]::IsNullOrWhiteSpace($identity.Id) -or
            [string]::IsNullOrWhiteSpace($identity.Version) -or
            -not $displayName) {
            throw "VSIX '$Path' has incomplete identity metadata."
        }

        return [pscustomobject]@{
            Id = [string]$identity.Id
            Version = [string]$identity.Version
            DisplayName = [string]$displayName.InnerText
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-T11AdapterPackageEvidence {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string[]] $ExpectedNames,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $actual = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $expectedMembers = [Collections.Generic.List[string]]::new()
    $actualMembers = [Collections.Generic.List[string]]::new()
    $invalidMembers = [Collections.Generic.List[string]]::new()
    $listing = [Collections.Generic.List[object]]::new()
    foreach ($name in $ExpectedNames) {
        $displayName = if ([string]::IsNullOrWhiteSpace($name)) {
            "<empty>"
        }
        else {
            $name
        }
        $expectedMembers.Add($displayName)
        if ([string]::IsNullOrWhiteSpace($name)) {
            $invalidMembers.Add("invalid expected: $displayName")
        }
        elseif (-not $expected.Add($name)) {
            $invalidMembers.Add("duplicate expected: $name")
        }
    }

    $archive = $null
    $entryCount = 0
    $errorMessage = $null
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            throw "TestAdapter archive '$Path' was not found."
        }

        $archive = [IO.Compression.ZipFile]::OpenRead($Path)
        foreach ($entry in $archive.Entries) {
            $entryCount++
            $fullName = [string]$entry.FullName
            $displayName = if ([string]::IsNullOrWhiteSpace($fullName)) {
                "<empty>"
            }
            else {
                $fullName
            }
            $actualMembers.Add(
                "$displayName`tByteLength=$($entry.Length)`tCompressedByteLength=$($entry.CompressedLength)")
            $listing.Add([ordered]@{
                    Name = $entry.Name
                    ByteLength = $entry.Length
                    CompressedByteLength = $entry.CompressedLength
                })

            if ([string]::IsNullOrWhiteSpace($entry.Name) -or
                $entry.FullName -cne $entry.Name) {
                $invalidMembers.Add("invalid entry: $displayName")
            }
            if (-not $actual.Add($fullName)) {
                $invalidMembers.Add("duplicate entry: $displayName")
            }
            if ($entry.Length -le 0) {
                $invalidMembers.Add("empty entry: $displayName")
            }
        }
    }
    catch {
        $errorMessage = $_.Exception.Message
    }
    finally {
        if ($archive) {
            $archive.Dispose()
        }
    }

    $missing = [Collections.Generic.List[string]]::new()
    foreach ($name in $expected) {
        if (-not $actual.Contains($name)) {
            $missing.Add($name)
        }
    }
    $extra = [Collections.Generic.List[string]]::new()
    foreach ($name in $actual) {
        if (-not $expected.Contains($name)) {
            $extra.Add($name)
        }
    }

    $expectedMembers.Sort([StringComparer]::Ordinal)
    $actualMembers.Sort([StringComparer]::Ordinal)
    $missing.Sort([StringComparer]::Ordinal)
    $extra.Sort([StringComparer]::Ordinal)
    $invalidMembers.Sort([StringComparer]::Ordinal)
    if (-not $errorMessage -and
        ($missing.Count -gt 0 -or
        $extra.Count -gt 0 -or
        $invalidMembers.Count -gt 0 -or
        $entryCount -ne $expected.Count)) {
        $errorMessage =
            "TestAdapter archive membership does not match the canonical package list."
    }

    $status = if ($errorMessage) { "Failed" } else { "Passed" }
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("Status: $status")
    $lines.Add("Error: $(if ($errorMessage) { $errorMessage } else { '<none>' })")
    foreach ($section in @(
            [pscustomobject]@{ Name = "Expected"; Values = $expectedMembers },
            [pscustomobject]@{ Name = "Actual"; Values = $actualMembers },
            [pscustomobject]@{ Name = "Missing"; Values = $missing },
            [pscustomobject]@{ Name = "Extra"; Values = $extra },
            [pscustomobject]@{
                Name = "DuplicateOrInvalid"
                Values = $invalidMembers
            }
        )) {
        $lines.Add("$($section.Name):")
        if ($section.Values.Count -eq 0) {
            $lines.Add("<none>")
        }
        else {
            foreach ($value in $section.Values) {
                $lines.Add([string]$value)
            }
        }
    }
    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $reportDirectory -Force)
    }
    [IO.File]::WriteAllLines(
        $ReportPath,
        $lines,
        [Text.UTF8Encoding]::new($false))

    if ($errorMessage) {
        throw $errorMessage
    }
    return @($listing)
}

function Get-T11HostSelection {
    param (
        [Parameter(Mandatory)]
        [object[]] $Instances,

        [Parameter(Mandatory)]
        [string[]] $CoreEditorInstanceIds,

        [Parameter(Mandatory)]
        [ValidateSet(17, 18)]
        [int] $VisualStudioMajorVersion,

        [Parameter(Mandatory)]
        [version] $MinimumVersion,

        [Parameter(Mandatory)]
        [version] $MaximumVersion
    )

    $allowedProducts = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    @(
        "Microsoft.VisualStudio.Product.Community",
        "Microsoft.VisualStudio.Product.Professional",
        "Microsoft.VisualStudio.Product.Enterprise"
    ) | ForEach-Object { [void]$allowedProducts.Add($_) }

    $coreIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $CoreEditorInstanceIds) {
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            [void]$coreIds.Add($id)
        }
    }

    $instanceIdCounts = @{}
    foreach ($instance in $Instances) {
        $id = [string]$instance.instanceId
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            $key = $id.ToUpperInvariant()
            if (-not $instanceIdCounts.ContainsKey($key)) {
                $instanceIdCounts[$key] = 0
            }
            $instanceIdCounts[$key]++
        }
    }

    $decisions = [Collections.Generic.List[object]]::new()
    $candidates = [Collections.Generic.List[object]]::new()
    foreach ($instance in $Instances) {
        $reasons = [Collections.Generic.List[string]]::new()
        $instanceId = [string]$instance.instanceId
        $installationPath = [string]$instance.installationPath
        $installationVersion = [string]$instance.installationVersion
        $productId = [string]$instance.productId
        $productPath = [string]$instance.productPath
        $version = $null

        if ([string]::IsNullOrWhiteSpace($instanceId)) {
            $reasons.Add("Missing instance ID.")
        }
        elseif ($instanceIdCounts[$instanceId.ToUpperInvariant()] -ne 1) {
            $reasons.Add("Duplicate instance ID.")
        }

        if (-not $coreIds.Contains($instanceId)) {
            $reasons.Add("Core Editor component is missing.")
        }
        if ($instance.isComplete -isnot [bool] -or -not $instance.isComplete) {
            $reasons.Add("Installation is incomplete.")
        }
        if ($instance.isLaunchable -isnot [bool] -or -not $instance.isLaunchable) {
            $reasons.Add("Installation is not launchable.")
        }
        if (-not $allowedProducts.Contains($productId)) {
            $reasons.Add("Product is not Community, Professional, or Enterprise.")
        }

        if (-not [version]::TryParse($installationVersion, [ref]$version)) {
            $reasons.Add("Installation version is invalid.")
        }
        else {
            if ($version.Major -ne $VisualStudioMajorVersion) {
                $reasons.Add("Installation major is not $VisualStudioMajorVersion.")
            }
            if ($version -lt $MinimumVersion) {
                $reasons.Add("Installation version is below $MinimumVersion.")
            }
            if ($version -ge $MaximumVersion) {
                $reasons.Add("Installation version is not below $MaximumVersion.")
            }
        }

        $devenvPath = $null
        $vsixInstallerPath = $null
        $vstestPath = $null
        if ([string]::IsNullOrWhiteSpace($installationPath)) {
            $reasons.Add("Installation path is missing.")
        }
        else {
            $installationPath = [IO.Path]::GetFullPath($installationPath)
            $devenvPath = Join-Path $installationPath "Common7\IDE\devenv.exe"
            $vsixInstallerPath = Join-Path $installationPath "Common7\IDE\VSIXInstaller.exe"
            $vstestPath = Join-Path $installationPath `
                "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"

            if ([string]::IsNullOrWhiteSpace($productPath) -or
                [IO.Path]::GetFullPath($productPath) -ne
                    [IO.Path]::GetFullPath($devenvPath)) {
                $reasons.Add("Product path does not identify the selected devenv.exe.")
            }
            foreach ($tool in @($devenvPath, $vsixInstallerPath, $vstestPath)) {
                if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
                    $reasons.Add("Required selected-host tool is missing: $tool")
                }
            }
        }

        $accepted = $reasons.Count -eq 0
        $decision = [ordered]@{
            InstanceId = $instanceId
            InstallationPath = $installationPath
            InstallationVersion = $installationVersion
            ProductId = $productId
            ProductPath = $productPath
            IsComplete = $instance.isComplete
            IsLaunchable = $instance.isLaunchable
            HasCoreEditor = $coreIds.Contains($instanceId)
            Accepted = $accepted
            RejectionReasons = @($reasons)
            DevenvPath = $devenvPath
            VsixInstallerPath = $vsixInstallerPath
            VSTestPath = $vstestPath
        }
        $decisions.Add($decision)
        if ($accepted) {
            $candidates.Add([pscustomobject]$decision)
        }
    }

    $knownIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($instance in $Instances) {
        if (-not [string]::IsNullOrWhiteSpace([string]$instance.instanceId)) {
            [void]$knownIds.Add([string]$instance.instanceId)
        }
    }
    $unknownCoreIds = @($coreIds | Where-Object { -not $knownIds.Contains($_) })

    return [pscustomobject]@{
        Decisions = @($decisions)
        Candidates = @($candidates)
        UnknownCoreEditorInstanceIds = $unknownCoreIds
    }
}

function Get-T11DescendantProcessIds {
    param (
        [Parameter(Mandatory)]
        [int] $RootProcessId
    )

    $processes = @(Get-CimInstance Win32_Process |
            Select-Object ProcessId, ParentProcessId)
    $parents = [Collections.Generic.Queue[int]]::new()
    $parents.Enqueue($RootProcessId)
    $result = [Collections.Generic.List[int]]::new()
    while ($parents.Count -gt 0) {
        $parent = $parents.Dequeue()
        foreach ($process in $processes | Where-Object {
                [int]$_.ParentProcessId -eq $parent
            }) {
            $id = [int]$process.ProcessId
            if (-not $result.Contains($id)) {
                $result.Add($id)
                $parents.Enqueue($id)
            }
        }
    }
    return @($result)
}

function Stop-T11ProcessTree {
    param (
        [Parameter(Mandatory)]
        [int] $RootProcessId
    )

    $descendants = @(Get-T11DescendantProcessIds -RootProcessId $RootProcessId)
    [array]::Reverse($descendants)
    $targets = @($descendants) + @($RootProcessId)
    $stopped = [Collections.Generic.List[int]]::new()
    foreach ($id in $targets) {
        if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $id -Force -ErrorAction Stop
            $stopped.Add($id)
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $survivors = @($targets | Where-Object {
                Get-Process -Id $_ -ErrorAction SilentlyContinue
            })
        if ($survivors.Count -eq 0) {
            return @($stopped)
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "PID-scoped cleanup did not stop process IDs: $($survivors -join ', ')."
}

function Invoke-T11BoundedProcess {
    param (
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $StandardOutputPath,

        [Parameter(Mandatory)]
        [string] $StandardErrorPath,

        [Parameter(Mandatory)]
        [ValidateRange(1, 1800)]
        [int] $TimeoutSeconds,

        [string] $WorkingDirectory
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Process executable '$FilePath' was not found."
    }

    foreach ($path in @($StandardOutputPath, $StandardErrorPath)) {
        $directory = Split-Path -Parent $path
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            [void](New-Item -ItemType Directory -Path $directory -Force)
        }
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = $WorkingDirectory
    }
    foreach ($argument in $ArgumentList) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startedUtc = [DateTime]::UtcNow
    $timedOut = $false
    $stoppedProcessIds = @()
    $observedDescendantIds = [Collections.Generic.HashSet[int]]::new()
    $processId = $null
    try {
        if (-not $process.Start()) {
            throw "Failed to start '$FilePath'."
        }
        $processId = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $deadline = $startedUtc.AddSeconds($TimeoutSeconds)
        $nextObservation = [DateTime]::UtcNow
        while (-not $process.WaitForExit(250)) {
            if ([DateTime]::UtcNow -ge $nextObservation) {
                foreach ($id in Get-T11DescendantProcessIds `
                        -RootProcessId $processId) {
                    [void]$observedDescendantIds.Add($id)
                }
                $nextObservation = [DateTime]::UtcNow.AddSeconds(1)
            }
            if ([DateTime]::UtcNow -ge $deadline) {
                $timedOut = $true
                $stoppedProcessIds = @(Stop-T11ProcessTree -RootProcessId $processId)
                [void]$process.WaitForExit(10000)
                break
            }
        }

        $standardOutput = $stdoutTask.GetAwaiter().GetResult()
        $standardError = $stderrTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText(
            $StandardOutputPath,
            $standardOutput,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            $StandardErrorPath,
            $standardError,
            [Text.UTF8Encoding]::new($false))

        return [pscustomobject]@{
            FilePath = $FilePath
            Arguments = @($ArgumentList)
            ProcessId = $processId
            StartedUtc = $startedUtc.ToString("O")
            FinishedUtc = [DateTime]::UtcNow.ToString("O")
            TimeoutSeconds = $TimeoutSeconds
            ExitCode = if ($process.HasExited) { $process.ExitCode } else { $null }
            TimedOut = $timedOut
            StoppedProcessIds = $stoppedProcessIds
            ObservedDescendantProcessIds = @($observedDescendantIds)
        }
    }
    catch {
        $failure = $_
        $cleanupFailure = $null
        if ($processId -and
            (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
            try {
                [void](Stop-T11ProcessTree -RootProcessId $processId)
            }
            catch {
                $cleanupFailure = $_.Exception.Message
            }
        }
        foreach ($path in @($StandardOutputPath, $StandardErrorPath)) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                [IO.File]::WriteAllText(
                    $path,
                    "",
                    [Text.UTF8Encoding]::new($false))
            }
        }
        if ($cleanupFailure) {
            throw "$($failure.Exception.Message) PID-scoped cleanup also failed: $cleanupFailure"
        }
        throw $failure
    }
    finally {
        $process.Dispose()
    }
}

function Resolve-T11VisualStudioHost {
    param (
        [Parameter(Mandatory)]
        [ValidateSet(17, 18)]
        [int] $VisualStudioMajorVersion,

        [Parameter(Mandatory)]
        [version] $MinimumVersion,

        [Parameter(Mandatory)]
        [version] $MaximumVersion,

        [Parameter(Mandatory)]
        [string] $DiagnosticsDirectory,

        [Parameter(Mandatory)]
        [string] $ReportPath,

        [string] $VsWherePath = (Join-Path ${env:ProgramFiles(x86)} `
                "Microsoft Visual Studio\Installer\vswhere.exe")
    )

    $report = [ordered]@{
        Status = "Failed"
        VisualStudioMajorVersion = $VisualStudioMajorVersion
        MinimumVersion = $MinimumVersion.ToString()
        MaximumVersion = $MaximumVersion.ToString()
        VsWherePath = $VsWherePath
        Queries = @()
        DiscoveredInstances = @()
        UnknownCoreEditorInstanceIds = @()
        SelectedInstance = $null
        Error = $null
    }

    try {
        if (-not (Test-Path -LiteralPath $VsWherePath -PathType Leaf)) {
            throw "vswhere was not found at '$VsWherePath'."
        }

        $queries = @(
            [pscustomobject]@{
                Name = "all"
                Arguments = @(
                    "-all",
                    "-prerelease",
                    "-products",
                    "*",
                    "-format",
                    "json",
                    "-utf8")
            },
            [pscustomobject]@{
                Name = "core-editor"
                Arguments = @(
                    "-all",
                    "-prerelease",
                    "-products",
                    "*",
                    "-requires",
                    "Microsoft.VisualStudio.Component.CoreEditor",
                    "-format",
                    "json",
                    "-utf8")
            })

        $outputs = @{}
        $queryEvidence = [Collections.Generic.List[object]]::new()
        foreach ($query in $queries) {
            $stdoutPath = Join-Path $DiagnosticsDirectory "vswhere-$($query.Name).json"
            $stderrPath = Join-Path $DiagnosticsDirectory "vswhere-$($query.Name).stderr.log"
            $result = Invoke-T11BoundedProcess `
                -FilePath $VsWherePath `
                -ArgumentList $query.Arguments `
                -StandardOutputPath $stdoutPath `
                -StandardErrorPath $stderrPath `
                -TimeoutSeconds 30
            $queryEvidence.Add($result)
            $report.Queries = @($queryEvidence)
            if ($result.TimedOut) {
                throw "The vswhere '$($query.Name)' query timed out."
            }
            if ($result.ExitCode -ne 0) {
                throw "The vswhere '$($query.Name)' query exited with code $($result.ExitCode)."
            }

            $raw = Get-Content -LiteralPath $stdoutPath -Raw
            if ([string]::IsNullOrWhiteSpace($raw)) {
                throw "The vswhere '$($query.Name)' query returned no JSON."
            }
            $outputs[$query.Name] = @($raw | ConvertFrom-Json)
        }
        $coreIds = @($outputs["core-editor"] | ForEach-Object {
                [string]$_.instanceId
            })
        $selection = Get-T11HostSelection `
            -Instances @($outputs["all"]) `
            -CoreEditorInstanceIds $coreIds `
            -VisualStudioMajorVersion $VisualStudioMajorVersion `
            -MinimumVersion $MinimumVersion `
            -MaximumVersion $MaximumVersion
        $report.DiscoveredInstances = $selection.Decisions
        $report.UnknownCoreEditorInstanceIds =
            $selection.UnknownCoreEditorInstanceIds

        if ($selection.UnknownCoreEditorInstanceIds.Count -gt 0) {
            throw "The Core Editor query returned unknown Visual Studio instances."
        }
        if ($selection.Candidates.Count -ne 1) {
            throw "Expected exactly one complete Visual Studio $VisualStudioMajorVersion Core Editor installation in [$MinimumVersion,$MaximumVersion); found $($selection.Candidates.Count)."
        }

        $report.Status = "Passed"
        $report.SelectedInstance = $selection.Candidates[0]
        return $selection.Candidates[0]
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function Get-T11InstalledExtensionEvidence {
    param (
        [Parameter(Mandatory)]
        [string] $ProfileParent,

        [Parameter(Mandatory)]
        [string] $RootSuffix,

        [Parameter(Mandatory)]
        [string] $ExtensionId,

        [Parameter(Mandatory)]
        [string] $ExtensionVersion,

        [Parameter(Mandatory)]
        [string] $ReportPath,

        [Parameter(Mandatory)]
        [ValidateRange(1, 120)]
        [int] $TimeoutSeconds
    )

    $report = [ordered]@{
        Status = "Failed"
        ProfileParent = $ProfileParent
        RootSuffix = $RootSuffix
        ExpectedId = $ExtensionId
        ExpectedVersion = $ExtensionVersion
        Profiles = @()
        Manifests = @()
        InstalledManifest = $null
        Error = $null
    }

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        do {
            $profiles = @()
            if (Test-Path -LiteralPath $ProfileParent -PathType Container) {
                $profiles = @(Get-ChildItem -LiteralPath $ProfileParent -Directory |
                        Where-Object {
                            $_.Name.EndsWith(
                                $RootSuffix,
                                [StringComparison]::OrdinalIgnoreCase)
                        })
            }
            $report.Profiles = @($profiles.FullName)
            if ($profiles.Count -gt 1) {
                throw "The root suffix '$RootSuffix' resolved to multiple Visual Studio profiles."
            }

            if ($profiles.Count -eq 1) {
                $manifestEvidence = [Collections.Generic.List[object]]::new()
                $matches = [Collections.Generic.List[object]]::new()
                foreach ($manifestPath in Get-ChildItem `
                        -LiteralPath $profiles[0].FullName `
                        -Filter "extension.vsixmanifest" `
                        -File `
                        -Recurse) {
                    try {
                        [xml]$manifest = Get-Content -LiteralPath $manifestPath.FullName -Raw
                        $identity = $manifest.SelectSingleNode(
                            "/*[local-name()='PackageManifest']/*[local-name()='Metadata']/*[local-name()='Identity']")
                        $item = [ordered]@{
                            Path = $manifestPath.FullName
                            Id = if ($identity) { [string]$identity.Id } else { $null }
                            Version = if ($identity) {
                                [string]$identity.Version
                            }
                            else {
                                $null
                            }
                            Error = $null
                        }
                    }
                    catch {
                        $item = [ordered]@{
                            Path = $manifestPath.FullName
                            Id = $null
                            Version = $null
                            Error = $_.Exception.Message
                        }
                    }
                    $manifestEvidence.Add($item)
                    if ($item.Id -ceq $ExtensionId) {
                        $matches.Add($item)
                    }
                }
                $report.Manifests = @($manifestEvidence)

                if ($matches.Count -gt 1) {
                    throw "The isolated Visual Studio profile contains multiple '$ExtensionId' manifests."
                }
                if ($matches.Count -eq 1) {
                    if ($matches[0].Version -cne $ExtensionVersion) {
                        throw "Installed extension version '$($matches[0].Version)' does not match '$ExtensionVersion'."
                    }

                    $report.Status = "Passed"
                    $report.InstalledManifest = $matches[0]
                    return [pscustomobject]@{
                        ProfilePath = $profiles[0].FullName
                        ManifestPath = $matches[0].Path
                        Id = $matches[0].Id
                        Version = $matches[0].Version
                    }
                }
            }

            Start-Sleep -Milliseconds 500
        } while ([DateTime]::UtcNow -lt $deadline)

        throw "Installed extension '$ExtensionId' was not found in the isolated '$RootSuffix' profile."
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function Assert-T11OwnedProfilePath {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership,

        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not $Ownership.CollisionChecked) {
        throw "Visual Studio profile cleanup requires a completed collision check."
    }

    $profileParent = [IO.Path]::GetFullPath([string]$Ownership.ProfileParent)
    $profilePath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.Path]::GetFullPath(
            [IO.Path]::GetDirectoryName($profilePath)).Equals(
            $profileParent,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($profilePath).EndsWith(
            [string]$Ownership.RootSuffix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Visual Studio profile ownership must name one exact root-suffix profile."
    }

    return $profilePath
}

function New-T11ProfileOwnership {
    param (
        [Parameter(Mandatory)]
        [string] $ProfileParent,

        [Parameter(Mandatory)]
        [ValidatePattern("^[A-Za-z][A-Za-z0-9]{5,63}$")]
        [string] $RootSuffix
    )

    $profileParent = [IO.Path]::GetFullPath($ProfileParent)
    $existingProfiles = @()
    if (Test-Path -LiteralPath $profileParent -PathType Container) {
        $existingProfiles = @(Get-ChildItem -LiteralPath $profileParent -Directory |
                Where-Object {
                    $_.Name.EndsWith(
                        $RootSuffix,
                        [StringComparison]::OrdinalIgnoreCase)
                })
    }
    if ($existingProfiles.Count -gt 0) {
        throw "The supposedly unique root suffix '$RootSuffix' already exists."
    }

    return [pscustomobject]@{
        ProfileParent = $profileParent
        RootSuffix = $RootSuffix
        CollisionChecked = $true
        CleanupEligible = $false
        OwnedProfilePath = $null
        Removed = $false
    }
}

function Set-T11OwnedProfile {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership,

        [Parameter(Mandatory)]
        [string] $ProfilePath
    )

    $profilePath = Assert-T11OwnedProfilePath `
        -Ownership $Ownership `
        -Path $ProfilePath
    if (-not (Test-Path -LiteralPath $profilePath -PathType Container)) {
        throw "The run-owned Visual Studio profile '$profilePath' was not found."
    }
    if ($Ownership.CleanupEligible -and
        -not $Ownership.OwnedProfilePath.Equals(
            $profilePath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Visual Studio profile ownership cannot change after it is established."
    }

    $Ownership.OwnedProfilePath = $profilePath
    $Ownership.CleanupEligible = $true
    return $profilePath
}

function Remove-T11OwnedProfile {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    if (-not $Ownership.CleanupEligible -or
        [string]::IsNullOrWhiteSpace([string]$Ownership.OwnedProfilePath)) {
        return $false
    }

    $profilePath = Assert-T11OwnedProfilePath `
        -Ownership $Ownership `
        -Path ([string]$Ownership.OwnedProfilePath)
    $removed = Test-Path -LiteralPath $profilePath -PathType Container
    if ($removed) {
        Remove-Item -LiteralPath $profilePath -Recurse -Force
    }
    if (Test-Path -LiteralPath $profilePath) {
        throw "The run-owned Visual Studio profile remains after cleanup."
    }

    $Ownership.Removed = $true
    return $removed
}

function Test-T11PackageLoadFault {
    param (
        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string[]] $MatchedScopeTokens
    )

    if ($Text -match
        "(?i)\bPackage Load (?:Failure|Failed|Error)\b|\bSetSite (?:failed|failure) for package\b|\bCreateInstance failed for package\b") {
        return $true
    }

    $failure = "(?:failed to load|did not load(?: correctly)?|could not be loaded|cannot be loaded)"
    foreach ($token in $MatchedScopeTokens) {
        $scope = [regex]::Escape($token)
        if ($Text -match
            "(?i)(?:the\s+)?['""]?$scope['""]?\s+package\s+$failure[.!]?\s*$|\bpackage\s+['""]?$scope['""]?\s+$failure[.!]?\s*$") {
            return $true
        }
    }

    return $false
}

function Get-T11ActivityLogAnalysis {
    param (
        [Parameter(Mandatory)]
        [string] $ActivityLogPath,

        [Parameter(Mandatory)]
        [string[]] $ScopeTokens,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $report = [ordered]@{
        Status = "Failed"
        ActivityLogPath = $ActivityLogPath
        EntryCount = 0
        ErrorCount = 0
        ScopedErrors = @()
        BlockingErrorCount = 0
        BlockingErrors = @()
        Error = $null
    }

    try {
        if (-not (Test-Path -LiteralPath $ActivityLogPath -PathType Leaf) -or
            (Get-Item -LiteralPath $ActivityLogPath).Length -le 0) {
            throw "Visual Studio did not produce a non-empty ActivityLog.xml."
        }

        [xml]$activityLog = Get-Content -LiteralPath $ActivityLogPath -Raw
        $entries = @($activityLog.SelectNodes("//*[local-name()='entry']"))
        $report.EntryCount = $entries.Count
        $errors = @($entries | Where-Object {
                $type = $_.SelectSingleNode("*[local-name()='type']")
                $type -and $type.InnerText.Equals(
                    "Error",
                    [StringComparison]::OrdinalIgnoreCase)
            })
        $report.ErrorCount = $errors.Count

        $scoped = [Collections.Generic.List[object]]::new()
        $blocking = [Collections.Generic.List[object]]::new()
        foreach ($entry in $errors) {
            $text = $entry.InnerText
            $matchedTokens = @($ScopeTokens | Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_) -and
                    $text.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
                })
            if ($matchedTokens.Count -eq 0) {
                continue
            }

            $getValue = {
                param ([string] $Name)
                $node = $entry.SelectSingleNode("*[local-name()='$Name']")
                if ($node) { return [string]$node.InnerText }
                return $null
            }
            $description = & $getValue "description"
            $packageFaultText = [string]$description
            $category = if ($text -match "(?i)\bregistration\b|\bpkgdef\b|(?:failed|failure|error|exception).{0,80}\bregister(?:ed|ing)?\b|\bregister(?:ed|ing)?\b.{0,80}(?:failed|failure|error|exception)") {
                "Registration"
            }
            elseif ($text -match "(?i)\bcomposition\b|\bMEF\b|CompositionException|ComposablePart") {
                "Composition"
            }
            elseif ($text -match "(?i)\bassembly binding\b|(?:could not|cannot|failed to|unable to) load (?:file or )?assembly|\bassembly load (?:failed|failure|error)\b|FileLoadException|FileNotFoundException|BadImageFormatException|\bfusion log\b") {
                "Binding"
            }
            elseif (Test-T11PackageLoadFault `
                    -Text $packageFaultText `
                    -MatchedScopeTokens @($matchedTokens | Where-Object {
                            $packageFaultText.IndexOf(
                                $_,
                                [StringComparison]::OrdinalIgnoreCase) -ge 0
                        })) {
                "PackageLoad"
            }
            else {
                $null
            }

            $scopedError = [ordered]@{
                    Record = & $getValue "record"
                    Time = & $getValue "time"
                    Source = & $getValue "source"
                    Description = $description
                    Guid = & $getValue "guid"
                    Category = $category
                    BlocksValidation = $null -ne $category
                    MatchedTokens = $matchedTokens
                }
            $scoped.Add($scopedError)
            if ($category) {
                $blocking.Add($scopedError)
            }
        }
        $report.ScopedErrors = @($scoped)
        $report.BlockingErrorCount = $blocking.Count
        $report.BlockingErrors = @($blocking)
        if ($blocking.Count -gt 0) {
            throw "ActivityLog.xml contains $($blocking.Count) approved main-extension fault(s)."
        }

        $report.Status = "Passed"
        return [pscustomobject]$report
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

Export-ModuleMember -Function `
    Get-T11ArtifactDefinitions, `
    New-T11ArtifactManifest, `
    Test-T11ArtifactTransport, `
    Get-T11VsixIdentity, `
    Get-T11AdapterPackageEvidence, `
    Get-T11HostSelection, `
    Stop-T11ProcessTree, `
    Invoke-T11BoundedProcess, `
    Resolve-T11VisualStudioHost, `
    Get-T11InstalledExtensionEvidence, `
    New-T11ProfileOwnership, `
    Set-T11OwnedProfile, `
    Remove-T11OwnedProfile, `
    Get-T11ActivityLogAnalysis
