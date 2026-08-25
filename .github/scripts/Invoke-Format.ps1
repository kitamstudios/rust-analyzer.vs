#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [ValidateSet("Fix", "Check")]
    [string] $Mode = "Check"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$extensions = @(
    ".config",
    ".cs",
    ".csproj",
    ".json",
    ".props",
    ".ps1",
    ".psm1",
    ".resx",
    ".ruleset",
    ".sln",
    ".targets",
    ".toml",
    ".vsext",
    ".vsct",
    ".vsixmanifest",
    ".xml",
    ".yaml",
    ".yml"
)
$configFileNames = @(
    ".editorconfig",
    ".gitattributes",
    ".gitignore",
    ".globalconfig"
)

function Get-TextEncoding {
    param (
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) {
        return [pscustomobject]@{
            Encoding = [Text.UTF8Encoding]::new($false, $true)
            Preamble = [byte[]](0xEF, 0xBB, 0xBF)
        }
    }

    if ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) {
        return [pscustomobject]@{
            Encoding = [Text.UnicodeEncoding]::new($false, $false, $true)
            Preamble = [byte[]](0xFF, 0xFE)
        }
    }

    if ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xFE -and $Bytes[1] -eq 0xFF) {
        return [pscustomobject]@{
            Encoding = [Text.UnicodeEncoding]::new($true, $false, $true)
            Preamble = [byte[]](0xFE, 0xFF)
        }
    }

    return [pscustomobject]@{
        Encoding = [Text.UTF8Encoding]::new($false, $true)
        Preamble = [byte[]]@()
    }
}

function Get-FormattedBytes {
    param (
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    $encodingInfo = Get-TextEncoding -Bytes $Bytes
    $preambleLength = $encodingInfo.Preamble.Length
    $text = $encodingInfo.Encoding.GetString($Bytes, $preambleLength, $Bytes.Length - $preambleLength)

    $crlfCount = [regex]::Matches($text, "`r`n").Count
    $lfCount = [regex]::Matches($text, "(?<!`r)`n").Count
    $lineEnding = if ($crlfCount -gt $lfCount) { "`r`n" } else { "`n" }

    $formatted = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $formatted = [regex]::Replace($formatted, "[ `t]+(?=`n|$)", "")
    if ($lineEnding -eq "`r`n") {
        $formatted = $formatted.Replace("`n", "`r`n")
    }

    $contentBytes = $encodingInfo.Encoding.GetBytes($formatted)
    $formattedBytes = [byte[]]::new($encodingInfo.Preamble.Length + $contentBytes.Length)
    [Buffer]::BlockCopy($encodingInfo.Preamble, 0, $formattedBytes, 0, $encodingInfo.Preamble.Length)
    [Buffer]::BlockCopy($contentBytes, 0, $formattedBytes, $encodingInfo.Preamble.Length, $contentBytes.Length)
    return $formattedBytes
}

function Test-GeneratedOutputPath {
    param (
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $normalized = $RelativePath.Replace("\", "/")
    return $normalized -eq "_built" -or
        $normalized.StartsWith("_built/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized -match "(^|/)(bin|obj|target|TestResults|coverage|mutation-workers)(/|$)" -or
        $normalized.EndsWith(".trx", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.EndsWith(".vsix", [StringComparison]::OrdinalIgnoreCase)
}

$trackedFiles = @(& git -C $repoRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE."
}

$untrackedFiles = @(& git -C $repoRoot ls-files --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files --others failed with exit code $LASTEXITCODE."
}

$formatFiles = @($trackedFiles + $untrackedFiles | Sort-Object -Unique)
$changedFiles = [Collections.Generic.List[string]]::new()
foreach ($relativePath in $formatFiles) {
    if (Test-GeneratedOutputPath -RelativePath $relativePath) {
        continue
    }

    $fileName = [IO.Path]::GetFileName($relativePath)
    $extension = [IO.Path]::GetExtension($relativePath)
    if ($extensions -notcontains $extension -and $configFileNames -notcontains $fileName) {
        continue
    }

    if ($fileName -in @("source.extension.cs", "VSCommandTable.cs") -or
        $fileName.EndsWith(".Designer.cs", [StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith(".g.cs", [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $bytes = [IO.File]::ReadAllBytes($path)
    $formattedBytes = Get-FormattedBytes -Bytes $bytes
    if ([Collections.StructuralComparisons]::StructuralEqualityComparer.Equals($bytes, $formattedBytes)) {
        continue
    }

    $changedFiles.Add($relativePath)
    if ($Mode -eq "Fix") {
        [IO.File]::WriteAllBytes($path, $formattedBytes)
    }
}

if ($changedFiles.Count -eq 0) {
    Write-Host "Formatting is clean."
    return
}

$action = if ($Mode -eq "Fix") { "Formatted" } else { "Formatting drift" }
foreach ($relativePath in $changedFiles) {
    Write-Host "${action}: $relativePath"
}

if ($Mode -eq "Check") {
    throw "Formatting drift detected. Run Invoke-Format.ps1 -Mode Fix."
}
