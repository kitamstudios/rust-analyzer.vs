#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$manager = Join-Path $PSScriptRoot "Manage-RustAnalyzer.ps1"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$testRoot = Join-Path $repositoryRoot "_built\script-tests\rust-analyzer-provenance"
$oldRelease = "2026-08-24"
$newRelease = "2026-08-31"
$oldVersion = "rust-analyzer 0.3.1-standalone (1111111 2026-08-23)"
$newVersion = "rust-analyzer 0.3.2-standalone (2222222 2026-08-30)"
$target = "x86_64-pc-windows-msvc"
$assetName = "rust-analyzer-$target.zip"
$repository = "https://github.com/rust-lang/rust-analyzer"

function Get-Sha256 {
    param ([string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Manifest {
    param (
        [string] $Root,
        [string] $Release = $oldRelease,
        [string] $ReportedVersion = $oldVersion
    )

    $external = Join-Path $Root "src\external"
    $manifest = [ordered]@{
        schemaVersion = 1
        upstream = [ordered]@{
            repository = $repository
            release = $Release
            target = $target
        }
        assets = @(
            [ordered]@{
                name = $assetName
                url = "https://example.invalid/$Release/$assetName"
                officialSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                officialDigestSource = "https://api.github.com/repos/rust-lang/rust-analyzer/releases/assets/1"
                verifiedArchiveSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            })
        files = [ordered]@{
            executable = [ordered]@{
                name = "rust-analyzer.exe"
                sha256 = Get-Sha256 (Join-Path $external "rust-analyzer.exe")
            }
            pdb = [ordered]@{
                name = "rust_analyzer.pdb"
                sha256 = Get-Sha256 (Join-Path $external "rust_analyzer.pdb")
            }
        }
        reportedVersion = $ReportedVersion
    }
    $manifest | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $external "rust-analyzer.provenance.json") -Encoding utf8
}

function New-TestRepository {
    $root = Join-Path $testRoot ([Guid]::NewGuid())
    $external = Join-Path $root "src\external"
    $adapter = Join-Path $root "src\RustAnalyzer.TestAdapter"
    $extension = Join-Path $root "src\RustAnalyzer"
    New-Item -ItemType Directory -Path $external, $adapter, $extension | Out-Null
    Set-Content -LiteralPath (Join-Path $external "rust-analyzer.exe") -Value "old executable" -NoNewline
    Set-Content -LiteralPath (Join-Path $external "rust_analyzer.pdb") -Value "old pdb" -NoNewline
    Set-Content -LiteralPath (Join-Path $adapter "Constants.cs") -Value @"
public static class Constants
{
    public const string RlsLatestInPackageVersion = "$oldRelease";
}
"@
    Set-Content -LiteralPath (Join-Path $extension "RustAnalyzer.csproj") -Value @"
<Project>
  <ItemGroup>
    <Content Include="..\external\rust-analyzer.exe">
      <Link>$oldRelease\rust-analyzer.exe</Link>
    </Content>
    <Content Include="..\external\rust_analyzer.pdb">
      <Link>$oldRelease\rust_analyzer.pdb</Link>
    </Content>
    <Content Include="..\external\rust-analyzer.provenance.json">
      <Link>$oldRelease\rust-analyzer.provenance.json</Link>
    </Content>
  </ItemGroup>
</Project>
"@
    Write-Manifest $root

    return [PSCustomObject]@{
        Root = $root
        External = $external
        Manifest = Join-Path $external "rust-analyzer.provenance.json"
        Constants = Join-Path $adapter "Constants.cs"
        Project = Join-Path $extension "RustAnalyzer.csproj"
    }
}

function New-TestArchive {
    param (
        [string] $Path,
        [hashtable] $Entries
    )

    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entryName in $Entries.Keys) {
            $entry = $archive.CreateEntry($entryName)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try {
                $writer.Write($Entries[$entryName])
            }
            finally {
                $writer.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-Release {
    param (
        [string] $Release,
        [string] $ArchivePath,
        [string] $OfficialSha256 = $(if ($ArchivePath) {
                Get-Sha256 $ArchivePath
            }
            else {
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            })
    )

    return [PSCustomObject]@{
        Release = $Release
        Target = $target
        AssetName = $assetName
        AssetUrl = "https://example.invalid/$Release/$assetName"
        OfficialSha256 = $OfficialSha256
        OfficialDigestSource = "https://api.github.com/repos/rust-lang/rust-analyzer/releases/assets/2"
    }
}

function Assert-Failure {
    param (
        [scriptblock] $Action,
        [string] $Message
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") {
            throw "Expected failure containing '$Message', but got: $($_.Exception.Message)"
        }

        return
    }

    throw "Expected failure containing '$Message', but the action succeeded."
}

function New-VersionReader {
    param ([string] $Version)

    return { param ($Path) $Version }.GetNewClosure()
}

try {
    $repositoryState = New-TestRepository
    & $manager -Mode Verify -RepositoryRoot $repositoryState.Root -VersionReader (New-VersionReader $oldVersion)

    $repositoryState = New-TestRepository
    Set-Content -LiteralPath (Join-Path $repositoryState.External "rust-analyzer.exe") -Value "changed" -NoNewline
    $versionReads = [PSCustomObject]@{ Count = 0 }
    $countingVersionReader = {
        param ($Path)
        $versionReads.Count++
        $oldVersion
    }.GetNewClosure()
    Assert-Failure {
        & $manager -Mode Verify -RepositoryRoot $repositoryState.Root -VersionReader $countingVersionReader
    } "rust-analyzer.exe SHA-256"
    if ($versionReads.Count -ne 0) {
        throw "Verify executed rust-analyzer before validating its hash."
    }

    $repositoryState = New-TestRepository
    Set-Content -LiteralPath $repositoryState.Manifest -Value "{ malformed"
    Assert-Failure {
        & $manager -Mode Verify -RepositoryRoot $repositoryState.Root -VersionReader (New-VersionReader $oldVersion)
    } "manifest is malformed"

    $repositoryState = New-TestRepository
    Assert-Failure {
        & $manager -Mode Verify -RepositoryRoot $repositoryState.Root -VersionReader (New-VersionReader $newVersion)
    } "version does not match"

    $repositoryState = New-TestRepository
    (Get-Content -LiteralPath $repositoryState.Constants -Raw).Replace($oldRelease, "2026-08-17") |
        Set-Content -LiteralPath $repositoryState.Constants -NoNewline
    Assert-Failure {
        & $manager -Mode Verify -RepositoryRoot $repositoryState.Root -VersionReader (New-VersionReader $oldVersion)
    } "RlsLatestInPackageVersion"

    $repositoryState = New-TestRepository
    (Get-Content -LiteralPath $repositoryState.Project -Raw).Replace(
        "$oldRelease\rust-analyzer.exe",
        "2026-08-17\rust-analyzer.exe") |
        Set-Content -LiteralPath $repositoryState.Project -NoNewline
    Assert-Failure {
        & $manager -Mode Verify -RepositoryRoot $repositoryState.Root -VersionReader (New-VersionReader $oldVersion)
    } "Link paths"

    $repositoryState = New-TestRepository
    $calls = [Collections.Generic.List[string]]::new()
    $versionReader = {
        param ($Path)
        $calls.Add("verify")
        $oldVersion
    }.GetNewClosure()
    $sameRelease = New-Release $oldRelease $null
    $releaseResolver = {
        $calls.Add("resolve")
        $sameRelease
    }.GetNewClosure()
    & $manager -Mode Check -RepositoryRoot $repositoryState.Root -VersionReader $versionReader -ReleaseResolver $releaseResolver
    if (($calls -join ",") -ne "verify,resolve") {
        throw "Check did not run Verify before resolving latest metadata."
    }

    $repositoryState = New-TestRepository
    $staleRelease = New-Release $newRelease $null
    $staleResolver = { $staleRelease }.GetNewClosure()
    Assert-Failure {
        & $manager `
            -Mode Check `
            -RepositoryRoot $repositoryState.Root `
            -VersionReader (New-VersionReader $oldVersion) `
            -ReleaseResolver $staleResolver
    } "is stale"

    foreach ($failure in @(
            { throw "unavailable" },
            { throw [TimeoutException]::new("timeout") })) {
        $repositoryState = New-TestRepository
        Assert-Failure {
            & $manager `
                -Mode Check `
                -RepositoryRoot $repositoryState.Root `
                -VersionReader (New-VersionReader $oldVersion) `
                -ReleaseResolver $failure
        } "Refresh with:"
    }
    $repositoryState = New-TestRepository
    $repositoryState = New-TestRepository
    $missingDigestRelease = New-Release $oldRelease $null -OfficialSha256 ""
    $missingDigestResolver = { $missingDigestRelease }.GetNewClosure()
    Assert-Failure {
        & $manager `
            -Mode Check `
            -RepositoryRoot $repositoryState.Root `
            -VersionReader (New-VersionReader $oldVersion) `
            -ReleaseResolver $missingDigestResolver
    } "official"

    foreach ($unexpectedEntry in @("../outside.exe", "unexpected.txt")) {
        $repositoryState = New-TestRepository
        $archivePath = Join-Path $repositoryState.Root "unexpected.zip"
        New-TestArchive $archivePath @{
            "rust-analyzer.exe" = "new executable"
            "rust_analyzer.pdb" = "new pdb"
            $unexpectedEntry = "unexpected"
        }
        $release = New-Release $newRelease $archivePath
        $resolver = { $release }.GetNewClosure()
        $downloader = {
            param ($Url, $Destination, $TimeoutSeconds)
            Copy-Item -LiteralPath $archivePath -Destination $Destination
        }.GetNewClosure()
        Assert-Failure {
            & $manager `
                -Mode Update `
                -RepositoryRoot $repositoryState.Root `
                -VersionReader (New-VersionReader $newVersion) `
                -ReleaseResolver $resolver `
                -AssetDownloader $downloader
        } "unexpected or unsafe entry"
    }

    $repositoryState = New-TestRepository
    $archivePath = Join-Path $repositoryState.Root "valid.zip"
    New-TestArchive $archivePath @{
        "rust-analyzer.exe" = "new executable"
        "rust_analyzer.pdb" = "new pdb"
    }
    $release = New-Release $newRelease $archivePath
    $resolver = { $release }.GetNewClosure()
    $downloader = {
        param ($Url, $Destination, $TimeoutSeconds)
        Copy-Item -LiteralPath $archivePath -Destination $Destination
    }.GetNewClosure()
    $originalExecutableHash = Get-Sha256 (Join-Path $repositoryState.External "rust-analyzer.exe")
    $originalPdbHash = Get-Sha256 (Join-Path $repositoryState.External "rust_analyzer.pdb")
    $originalManifest = Get-Content -LiteralPath $repositoryState.Manifest -Raw
    $originalConstants = Get-Content -LiteralPath $repositoryState.Constants -Raw
    $originalProject = Get-Content -LiteralPath $repositoryState.Project -Raw
    $commitState = [PSCustomObject]@{ Count = 0 }
    $committer = {
        param ($Source, $Destination)
        $commitState.Count++
        if ($commitState.Count -eq 2) {
            throw "synthetic commit failure"
        }

        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }.GetNewClosure()
    Assert-Failure {
        & $manager `
            -Mode Update `
            -RepositoryRoot $repositoryState.Root `
            -VersionReader (New-VersionReader $newVersion) `
            -ReleaseResolver $resolver `
            -AssetDownloader $downloader `
            -FileCommitter $committer
    } "synthetic commit failure"
    if ((Get-Sha256 (Join-Path $repositoryState.External "rust-analyzer.exe")) -ne $originalExecutableHash -or
        (Get-Sha256 (Join-Path $repositoryState.External "rust_analyzer.pdb")) -ne $originalPdbHash -or
        (Get-Content -LiteralPath $repositoryState.Manifest -Raw) -ne $originalManifest -or
        (Get-Content -LiteralPath $repositoryState.Constants -Raw) -ne $originalConstants -or
        (Get-Content -LiteralPath $repositoryState.Project -Raw) -ne $originalProject) {
        throw "A failed Update did not restore every tracked input."
    }

    Write-Host "Manage-RustAnalyzer tests passed: 14."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
