#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet("Verify", "Check", "Update")]
    [string] $Mode,
    [string] $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..")),
    [scriptblock] $ReleaseResolver,
    [scriptblock] $AssetDownloader,
    [scriptblock] $VersionReader,
    [scriptblock] $FileCommitter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$externalDirectory = Join-Path $repositoryRoot "src\external"
$manifestPath = Join-Path $externalDirectory "rust-analyzer.provenance.json"
$executablePath = Join-Path $externalDirectory "rust-analyzer.exe"
$pdbPath = Join-Path $externalDirectory "rust_analyzer.pdb"
$constantsPath = Join-Path $repositoryRoot "src\RustAnalyzer.TestAdapter\Constants.cs"
$projectPath = Join-Path $repositoryRoot "src\RustAnalyzer\RustAnalyzer.csproj"
$temporaryDirectory = Join-Path $repositoryRoot "_built\rust-analyzer-update"
$target = "x86_64-pc-windows-msvc"
$assetName = "rust-analyzer-$target.zip"
$repository = "https://github.com/rust-lang/rust-analyzer"
$latestReleaseUrl = "https://api.github.com/repos/rust-lang/rust-analyzer/releases/latest"
$updateCommand = "pwsh -NoLogo -NoProfile -NonInteractive -File .\.github\scripts\Manage-RustAnalyzer.ps1 -Mode Update"

function Get-RequiredString {
    param (
        [object] $Object,
        [string] $Name,
        [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or
        $property.Value -isnot [string] -or
        [string]::IsNullOrWhiteSpace($property.Value)) {
        throw "The rust-analyzer provenance manifest has an invalid $Context.$Name."
    }

    return $property.Value
}

function Assert-Sha256 {
    param (
        [string] $Value,
        [string] $Context
    )

    if ($Value -notmatch "^[0-9a-fA-F]{64}$") {
        throw "The rust-analyzer provenance manifest has an invalid $Context SHA-256."
    }
}

function Get-Sha256 {
    param ([string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ReportedVersion {
    param ([string] $Path)

    if ($VersionReader) {
        $lines = @(& $VersionReader $Path)
    }
    else {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $Path
        $startInfo.Arguments = "--version"
        $startInfo.WorkingDirectory = Split-Path -Parent $Path
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        try {
            if (-not $process.Start()) {
                throw "The packaged rust-analyzer version command did not start."
            }

            $standardOutput = $process.StandardOutput.ReadToEnd()
            $null = $process.StandardError.ReadToEnd()
            if (-not $process.WaitForExit(30000)) {
                $process.Kill()
                throw "The packaged rust-analyzer version command timed out."
            }

            if ($process.ExitCode -ne 0) {
                throw "The packaged rust-analyzer version command failed."
            }

            $lines = @($standardOutput.TrimEnd("`r", "`n"))
        }
        finally {
            $process.Dispose()
        }
    }

    if ($lines.Count -ne 1 -or
        $lines[0] -isnot [string] -or
        $lines[0] -notmatch "^rust-analyzer \S+ \([0-9a-f]+ \d{4}-\d{2}-\d{2}\)$") {
        throw "The packaged rust-analyzer reported an invalid version."
    }

    return $lines[0]
}

function Get-ProjectLink {
    param (
        [xml] $Project,
        [string] $Include,
        [bool] $Required
    )

    $nodes = @($Project.SelectNodes("//*[local-name()='Content']") |
        Where-Object { $_.Include -eq $Include })
    if ($nodes.Count -eq 0 -and -not $Required) {
        return $null
    }

    if ($nodes.Count -ne 1 -or $null -eq $nodes[0].Link) {
        throw "RustAnalyzer.csproj must contain exactly one packaged entry for $Include."
    }

    return [string]$nodes[0].Link
}

function Read-ProvenanceManifest {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "The packaged rust-analyzer provenance manifest is missing."
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "The packaged rust-analyzer provenance manifest is malformed."
    }

    if ($manifest.schemaVersion -ne 1) {
        throw "The rust-analyzer provenance manifest schemaVersion must be 1."
    }

    if ($null -eq $manifest.upstream -or $null -eq $manifest.files) {
        throw "The rust-analyzer provenance manifest is missing required objects."
    }

    $upstreamRepository = Get-RequiredString $manifest.upstream "repository" "upstream"
    $release = Get-RequiredString $manifest.upstream "release" "upstream"
    $manifestTarget = Get-RequiredString $manifest.upstream "target" "upstream"
    $releaseDate = [DateTime]::MinValue
    if ($upstreamRepository -ne $repository -or
        $manifestTarget -ne $target -or
        $release -notmatch "^\d{4}-\d{2}-\d{2}$" -or
        -not [DateTime]::TryParseExact(
            $release,
            "yyyy-MM-dd",
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None,
            [ref] $releaseDate)) {
        throw "The rust-analyzer provenance manifest has invalid upstream identity."
    }

    $assets = @($manifest.assets)
    if ($assets.Count -ne 1) {
        throw "The rust-analyzer provenance manifest must contain exactly one release asset."
    }

    $asset = $assets[0]
    if ((Get-RequiredString $asset "name" "assets[0]") -ne $assetName) {
        throw "The rust-analyzer provenance manifest has an unexpected release asset."
    }

    foreach ($name in @("url", "officialDigestSource")) {
        $uri = $null
        if (-not [Uri]::TryCreate(
                (Get-RequiredString $asset $name "assets[0]"),
                [UriKind]::Absolute,
                [ref] $uri) -or
            $uri.Scheme -ne [Uri]::UriSchemeHttps) {
            throw "The rust-analyzer provenance manifest has an invalid assets[0].$name."
        }
    }

    $officialSha256 = Get-RequiredString $asset "officialSha256" "assets[0]"
    $verifiedArchiveSha256 = Get-RequiredString $asset "verifiedArchiveSha256" "assets[0]"
    Assert-Sha256 $officialSha256 "official"
    Assert-Sha256 $verifiedArchiveSha256 "verified archive"
    if ($officialSha256 -ne $verifiedArchiveSha256) {
        throw "The verified archive SHA-256 does not match the official SHA-256."
    }

    $executable = $manifest.files.executable
    if ($null -eq $executable -or
        (Get-RequiredString $executable "name" "files.executable") -ne "rust-analyzer.exe") {
        throw "The rust-analyzer provenance manifest has an invalid executable entry."
    }

    $executableSha256 = Get-RequiredString $executable "sha256" "files.executable"
    Assert-Sha256 $executableSha256 "executable"
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf) -or
        (Get-Sha256 $executablePath) -ne $executableSha256) {
        throw "The packaged rust-analyzer.exe SHA-256 does not match its provenance manifest."
    }

    $pdbProperty = $manifest.files.PSObject.Properties["pdb"]
    if ($null -ne $pdbProperty -and $null -ne $pdbProperty.Value) {
        $pdb = $pdbProperty.Value
        if ((Get-RequiredString $pdb "name" "files.pdb") -ne "rust_analyzer.pdb") {
            throw "The rust-analyzer provenance manifest has an invalid PDB entry."
        }

        $pdbSha256 = Get-RequiredString $pdb "sha256" "files.pdb"
        Assert-Sha256 $pdbSha256 "PDB"
        if (-not (Test-Path -LiteralPath $pdbPath -PathType Leaf) -or
            (Get-Sha256 $pdbPath) -ne $pdbSha256) {
            throw "The packaged rust_analyzer.pdb SHA-256 does not match its provenance manifest."
        }
    }
    elseif (Test-Path -LiteralPath $pdbPath) {
        throw "The packaged rust_analyzer.pdb is not recorded in its provenance manifest."
    }

    $reportedVersion = Get-RequiredString $manifest "reportedVersion" "root"
    $constants = Get-Content -LiteralPath $constantsPath -Raw
    $constantMatches = [regex]::Matches(
        $constants,
        'public const string RlsLatestInPackageVersion = "([^"]+)";')
    if ($constantMatches.Count -ne 1 -or $constantMatches[0].Groups[1].Value -ne $release) {
        throw "Constants.RlsLatestInPackageVersion does not match the packaged release."
    }

    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    if ((Get-ProjectLink $project "..\external\rust-analyzer.exe" $true) -ne "$release\rust-analyzer.exe" -or
        (Get-ProjectLink $project "..\external\rust_analyzer.pdb" ($null -ne $pdbProperty -and $null -ne $pdbProperty.Value)) -ne
            $(if ($null -ne $pdbProperty -and $null -ne $pdbProperty.Value) { "$release\rust_analyzer.pdb" } else { $null }) -or
        (Get-ProjectLink $project "..\external\rust-analyzer.provenance.json" $true) -ne "$release\rust-analyzer.provenance.json") {
        throw "RustAnalyzer.csproj packaged Link paths do not match the packaged release."
    }

    if ((Get-ReportedVersion $executablePath) -ne $reportedVersion) {
        throw "The packaged rust-analyzer version does not match its provenance manifest."
    }

    return $manifest
}

function Assert-Release {
    param ([object] $Release)

    if ($null -eq $Release) {
        throw "Official latest-release metadata was empty."
    }

    foreach ($name in @(
            "Release",
            "Target",
            "AssetName",
            "AssetUrl",
            "OfficialDigestSource")) {
        $property = $Release.PSObject.Properties[$name]
        if ($null -eq $property -or
            $property.Value -isnot [string] -or
            [string]::IsNullOrWhiteSpace($property.Value)) {
            throw "Official latest-release metadata has an invalid $name."
        }
    }

    $digestProperty = $Release.PSObject.Properties["OfficialSha256"]
    if ($null -eq $digestProperty -or
        $digestProperty.Value -isnot [string] -or
        [string]::IsNullOrWhiteSpace($digestProperty.Value)) {
        throw "Official latest-release metadata did not publish a SHA-256 digest for $assetName."
    }

    $releaseDate = [DateTime]::MinValue
    if ($Release.Release -notmatch "^\d{4}-\d{2}-\d{2}$" -or
        -not [DateTime]::TryParseExact(
            $Release.Release,
            "yyyy-MM-dd",
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None,
            [ref] $releaseDate) -or
        $Release.Target -ne $target -or
        $Release.AssetName -ne $assetName) {
        throw "Official latest-release metadata has an unexpected release, target, or asset."
    }

    Assert-Sha256 $Release.OfficialSha256 "official release"
    foreach ($name in @("AssetUrl", "OfficialDigestSource")) {
        $uri = $null
        if (-not [Uri]::TryCreate($Release.$name, [UriKind]::Absolute, [ref] $uri) -or
            $uri.Scheme -ne [Uri]::UriSchemeHttps) {
            throw "Official latest-release metadata has an invalid $name."
        }
    }

    return $Release
}

function Resolve-LatestRelease {
    if ($ReleaseResolver) {
        return Assert-Release (& $ReleaseResolver)
    }

    try {
        $response = Invoke-RestMethod `
            -Uri $latestReleaseUrl `
            -Headers @{
                Accept = "application/vnd.github+json"
                "User-Agent" = "rust-analyzer.vs-provenance"
            } `
            -TimeoutSec 30
    }
    catch {
        throw "Official latest-release metadata was unavailable or timed out."
    }

    $assets = @($response.assets | Where-Object { $_.name -ceq $assetName })
    if ($assets.Count -ne 1) {
        throw "Official latest-release metadata did not contain exactly one $assetName asset."
    }

    $asset = $assets[0]
    if ($asset.digest -notmatch "^sha256:([0-9a-fA-F]{64})$") {
        throw "Official latest-release metadata did not publish a SHA-256 digest for $assetName."
    }

    return Assert-Release ([PSCustomObject]@{
            Release = [string]$response.tag_name
            Target = $target
            AssetName = [string]$asset.name
            AssetUrl = [string]$asset.browser_download_url
            OfficialSha256 = $Matches[1].ToLowerInvariant()
            OfficialDigestSource = [string]$asset.url
        })
}

function Invoke-Check {
    try {
        $manifest = Read-ProvenanceManifest
        $latest = Resolve-LatestRelease
    }
    catch {
        throw "$($_.Exception.Message) Refresh with: $updateCommand"
    }

    $packagedDate = [DateTime]::ParseExact(
        $manifest.upstream.release,
        "yyyy-MM-dd",
        [Globalization.CultureInfo]::InvariantCulture)
    $latestDate = [DateTime]::ParseExact(
        $latest.Release,
        "yyyy-MM-dd",
        [Globalization.CultureInfo]::InvariantCulture)
    if ($latestDate -gt $packagedDate) {
        throw "Packaged rust-analyzer $($manifest.upstream.release) is stale; latest is $($latest.Release). Refresh with: $updateCommand"
    }

    if ($latestDate -eq $packagedDate -and
        $manifest.assets[0].officialSha256 -ne $latest.OfficialSha256) {
        throw "Official rust-analyzer metadata changed for the packaged release. Refresh with: $updateCommand"
    }

    Write-Host "Packaged rust-analyzer $($manifest.upstream.release) is current."
}

function Expand-VerifiedArchive {
    param (
        [string] $ArchivePath,
        [string] $Destination
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entries = @($archive.Entries)
        if (@($entries | Where-Object {
                    $_.FullName -cne "rust-analyzer.exe" -and
                    $_.FullName -cne "rust_analyzer.pdb"
                }).Count -ne 0) {
            throw "The official rust-analyzer archive contains an unexpected or unsafe entry."
        }

        $executableEntries = @($entries | Where-Object { $_.FullName -ceq "rust-analyzer.exe" })
        $pdbEntries = @($entries | Where-Object { $_.FullName -ceq "rust_analyzer.pdb" })
        if ($executableEntries.Count -ne 1 -or $pdbEntries.Count -gt 1) {
            throw "The official rust-analyzer archive has missing or duplicate expected entries."
        }

        foreach ($entry in @($executableEntries + $pdbEntries)) {
            $destinationPath = Join-Path $Destination $entry.FullName
            $input = $entry.Open()
            $output = [IO.File]::Create($destinationPath)
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    return [PSCustomObject]@{
        ExecutablePath = Join-Path $Destination "rust-analyzer.exe"
        PdbPath = if ($pdbEntries.Count -eq 1) {
            Join-Path $Destination "rust_analyzer.pdb"
        }
        else {
            $null
        }
    }
}

function Set-LinkPath {
    param (
        [string] $Content,
        [string] $Include,
        [string] $Link
    )

    $escapedInclude = [regex]::Escape($Include)
    $pattern = "(<Content Include=`"$escapedInclude`">\s*<Link>)[^<]+(</Link>)"
    $matches = [regex]::Matches($Content, $pattern)
    if ($matches.Count -ne 1) {
        throw "RustAnalyzer.csproj must contain exactly one packaged entry for $Include."
    }

    return [regex]::Replace(
        $Content,
        $pattern,
        { param ($match) $match.Groups[1].Value + $Link + $match.Groups[2].Value })
}

function Invoke-Update {
    try {
        $latest = Resolve-LatestRelease
    }
    catch {
        throw "$($_.Exception.Message) Refresh cannot continue."
    }

    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    try {
        $archivePath = Join-Path $temporaryDirectory $latest.AssetName
        if ($AssetDownloader) {
            $null = & $AssetDownloader $latest.AssetUrl $archivePath 30
        }
        else {
            Invoke-WebRequest `
                -Uri $latest.AssetUrl `
                -Headers @{ "User-Agent" = "rust-analyzer.vs-provenance" } `
                -OutFile $archivePath `
                -TimeoutSec 30
        }

        if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
            throw "The rust-analyzer archive download did not produce a file."
        }

        $archiveSha256 = Get-Sha256 $archivePath
        if ($archiveSha256 -ne $latest.OfficialSha256) {
            throw "The downloaded rust-analyzer archive SHA-256 does not match the official digest."
        }

        $stagedDirectory = Join-Path $temporaryDirectory "staged"
        New-Item -ItemType Directory -Path $stagedDirectory | Out-Null
        $staged = Expand-VerifiedArchive $archivePath $stagedDirectory
        $reportedVersion = Get-ReportedVersion $staged.ExecutablePath
        $executableSha256 = Get-Sha256 $staged.ExecutablePath
        $pdbSha256 = if ($staged.PdbPath) { Get-Sha256 $staged.PdbPath } else { $null }

        $files = [ordered]@{
            executable = [ordered]@{
                name = "rust-analyzer.exe"
                sha256 = $executableSha256
            }
        }
        if ($staged.PdbPath) {
            $files.pdb = [ordered]@{
                name = "rust_analyzer.pdb"
                sha256 = $pdbSha256
            }
        }

        $manifest = [ordered]@{
            schemaVersion = 1
            upstream = [ordered]@{
                repository = $repository
                release = $latest.Release
                target = $target
            }
            assets = @(
                [ordered]@{
                    name = $latest.AssetName
                    url = $latest.AssetUrl
                    officialSha256 = $latest.OfficialSha256
                    officialDigestSource = $latest.OfficialDigestSource
                    verifiedArchiveSha256 = $archiveSha256
                })
            files = $files
            reportedVersion = $reportedVersion
        }
        $stagedManifestPath = Join-Path $stagedDirectory "rust-analyzer.provenance.json"
        $manifest | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath $stagedManifestPath -Encoding utf8

        $constants = Get-Content -LiteralPath $constantsPath -Raw
        $constantPattern = 'public const string RlsLatestInPackageVersion = "([^"]+)";'
        if ([regex]::Matches($constants, $constantPattern).Count -ne 1) {
            throw "Constants.cs must contain exactly one packaged rust-analyzer version."
        }

        $stagedConstantsPath = Join-Path $stagedDirectory "Constants.cs"
        [regex]::Replace(
            $constants,
            $constantPattern,
            "public const string RlsLatestInPackageVersion = `"$($latest.Release)`";") |
            Set-Content -LiteralPath $stagedConstantsPath -Encoding utf8 -NoNewline

        $project = Get-Content -LiteralPath $projectPath -Raw
        $project = Set-LinkPath $project "..\external\rust-analyzer.exe" "$($latest.Release)\rust-analyzer.exe"
        if ($staged.PdbPath) {
            $project = Set-LinkPath $project "..\external\rust_analyzer.pdb" "$($latest.Release)\rust_analyzer.pdb"
        }
        else {
            $pdbPattern = '(?s)\s*<Content Include="\.\.\\external\\rust_analyzer\.pdb">.*?</Content>'
            if ([regex]::Matches($project, $pdbPattern).Count -ne 1) {
                throw "RustAnalyzer.csproj must contain exactly one packaged PDB entry."
            }

            $project = [regex]::Replace($project, $pdbPattern, "")
        }

        $project = Set-LinkPath $project "..\external\rust-analyzer.provenance.json" "$($latest.Release)\rust-analyzer.provenance.json"
        $stagedProjectPath = Join-Path $stagedDirectory "RustAnalyzer.csproj"
        Set-Content -LiteralPath $stagedProjectPath -Value $project -Encoding utf8 -NoNewline

        $changes = @(
            [PSCustomObject]@{ Source = $staged.ExecutablePath; Destination = $executablePath },
            [PSCustomObject]@{ Source = $staged.PdbPath; Destination = $pdbPath },
            [PSCustomObject]@{ Source = $stagedManifestPath; Destination = $manifestPath },
            [PSCustomObject]@{ Source = $stagedConstantsPath; Destination = $constantsPath },
            [PSCustomObject]@{ Source = $stagedProjectPath; Destination = $projectPath })
        $backupDirectory = Join-Path $temporaryDirectory "backup"
        New-Item -ItemType Directory -Path $backupDirectory | Out-Null
        $backups = @()
        for ($index = 0; $index -lt $changes.Count; $index++) {
            $destination = $changes[$index].Destination
            $existed = Test-Path -LiteralPath $destination -PathType Leaf
            $backup = Join-Path $backupDirectory $index
            if ($existed) {
                Copy-Item -LiteralPath $destination -Destination $backup
            }

            $backups += [PSCustomObject]@{
                Destination = $destination
                Existed = $existed
                Backup = $backup
            }
        }

        try {
            foreach ($change in $changes) {
                if ($FileCommitter) {
                    $null = & $FileCommitter $change.Source $change.Destination
                }
                elseif ($change.Source) {
                    Copy-Item -LiteralPath $change.Source -Destination $change.Destination -Force
                }
                elseif (Test-Path -LiteralPath $change.Destination) {
                    Remove-Item -LiteralPath $change.Destination -Force
                }
            }

            $null = Read-ProvenanceManifest
        }
        catch {
            foreach ($backup in $backups) {
                if ($backup.Existed) {
                    Copy-Item -LiteralPath $backup.Backup -Destination $backup.Destination -Force
                }
                elseif (Test-Path -LiteralPath $backup.Destination) {
                    Remove-Item -LiteralPath $backup.Destination -Force
                }
            }

            throw
        }

        Write-Host "Updated packaged rust-analyzer to $($latest.Release)."
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

switch ($Mode) {
    "Verify" {
        $manifest = Read-ProvenanceManifest
        Write-Host "Verified packaged rust-analyzer $($manifest.upstream.release)."
    }
    "Check" {
        Invoke-Check
    }
    "Update" {
        Invoke-Update
    }
}
