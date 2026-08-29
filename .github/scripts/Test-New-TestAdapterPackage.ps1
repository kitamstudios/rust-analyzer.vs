#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$testRoot = Join-Path $repoRoot "_built\script-tests\$([Guid]::NewGuid())"
$packageName = "KS.RustAnalyzer.TestAdapter.zip"
$manifestPath = Join-Path $repoRoot "src\RustAnalyzer.TestAdapter\testadapter-package.txt"
$expectedNames = @(
    "KS.RustAnalyzer.TestAdapter.dll",
    "KS.RustAnalyzer.TestAdapter.pdb",
    "Microsoft.ApplicationInsights.dll",
    "Microsoft.ApplicationInsights.pdb",
    "System.Collections.Immutable.dll",
    "Ensure.That.dll")

function New-TestRepository {
    param ([string[]] $Names)

    $root = Join-Path $testRoot ([Guid]::NewGuid())
    $scripts = Join-Path $root ".github\scripts"
    $source = Join-Path $root "src\RustAnalyzer.TestAdapter"
    $output = Join-Path $root "_built\projects\RustAnalyzer.TestAdapter"
    New-Item -ItemType Directory -Path $scripts, $source, $output | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "New-TestAdapterPackage.ps1") -Destination $scripts
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Get-TestAdapterPackageFile.ps1") -Destination $scripts
    $copiedManifestPath = Join-Path $source "testadapter-package.txt"
    Copy-Item -LiteralPath $manifestPath -Destination $copiedManifestPath
    if ($null -ne $Names) {
        Set-Content -LiteralPath $copiedManifestPath -Value $Names
    }

    $parsedNames = @(Get-Content -LiteralPath $copiedManifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })

    [PSCustomObject]@{
        Script = Join-Path $scripts "New-TestAdapterPackage.ps1"
        Output = $output
        Destination = Join-Path $output $packageName
        Names = $parsedNames
        Manifest = $copiedManifestPath
    }
}

function New-PackageInputs {
    param ($Repository)

    foreach ($name in $Repository.Names) {
        $path = Join-Path $Repository.Output $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value $name
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

try {
    $authoritativeNames = @(Get-Content -LiteralPath $manifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })
    if (Compare-Object -CaseSensitive -ReferenceObject $expectedNames -DifferenceObject $authoritativeNames) {
        throw "The authoritative TestAdapter package list is not the established six direct filenames."
    }

    $repository = New-TestRepository
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory (Join-Path $repository.Output "..\Other") `
            -DestinationPath $repository.Destination
    } "canonical owner path"

    $repository = New-TestRepository
    New-PackageInputs $repository
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory $repository.Output `
            -DestinationPath (Join-Path $repository.Output "..\$packageName")
    } "canonical owner directory"

    $repository = New-TestRepository
    New-PackageInputs $repository
    Remove-Item -LiteralPath (Join-Path $repository.Output $repository.Names[0])
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory $repository.Output `
            -DestinationPath $repository.Destination
    } "missing or is not a file"

    $repository = New-TestRepository
    New-PackageInputs $repository
    Set-Content -LiteralPath (Join-Path $repository.Output $repository.Names[0]) -Value $null
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory $repository.Output `
            -DestinationPath $repository.Destination
    } "package input is empty"

    foreach ($invalidName in @("..", "file.dll.")) {
        $repository = New-TestRepository -Names @($invalidName)
        Assert-Failure {
            & $repository.Script `
                -OutputDirectory $repository.Output `
                -DestinationPath $repository.Destination
        } $(if ($invalidName -eq "..") { "direct filename" } else { "changes after path normalization" })
    }

    $repository = New-TestRepository -Names @("subdirectory\file.dll")
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory $repository.Output `
            -DestinationPath $repository.Destination
    } "direct filename"

    $repository = New-TestRepository -Names @([IO.Path]::GetFullPath((Join-Path $testRoot "rooted.dll")))
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory $repository.Output `
            -DestinationPath $repository.Destination
    } "direct filename"

    $repository = New-TestRepository -Names @("duplicate.dll", "DUPLICATE.dll")
    Assert-Failure {
        & $repository.Script `
            -OutputDirectory $repository.Output `
            -DestinationPath $repository.Destination
    } "duplicate destination names"

    $repository = New-TestRepository
    New-PackageInputs $repository
    Set-Content -LiteralPath (Join-Path $repository.Output "unrelated.dll") -Value "unrelated"
    & $repository.Script `
        -OutputDirectory $repository.Output `
        -DestinationPath $repository.Destination

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($repository.Destination)
    try {
        $actualNames = @($archive.Entries.FullName | Sort-Object)
        $expectedArchiveNames = @($repository.Names | Sort-Object)
        if (Compare-Object -ReferenceObject $expectedArchiveNames -DifferenceObject $actualNames) {
            throw "The successful package did not contain the exact curated entry set."
        }

        if (@($archive.Entries | Where-Object Length -eq 0).Count -ne 0) {
            throw "The successful package contained an empty entry."
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host "New-TestAdapterPackage tests passed: 9."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
