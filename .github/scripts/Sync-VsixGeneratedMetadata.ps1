#Requires -PSEdition Core
#Requires -Version 7.1

[CmdletBinding()]
param (
    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$manifestPath = Join-Path $repoRoot "src\RustAnalyzer\source.extension.vsixmanifest"
$sourcePath = Join-Path $repoRoot "src\RustAnalyzer\source.extension.cs"
$manifestNamespace = "http://schemas.microsoft.com/developer/vsx-schema/2011"

function Get-SingleManifestValue {
    param (
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $XPath
    )

    $nodes = $manifest.SelectNodes($XPath, $namespaceManager)
    if ($nodes.Count -ne 1) {
        throw "Expected exactly one '$Name' in $manifestPath, found $($nodes.Count)."
    }

    $node = $nodes[0]
    $value = if ($node.NodeType -eq [System.Xml.XmlNodeType]::Attribute) {
        $node.Value
    }
    else {
        $node.InnerText
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "'$Name' in $manifestPath must not be empty."
    }

    return $value
}

function ConvertTo-CSharpStringLiteral {
    param (
        [Parameter(Mandatory)]
        [string] $Value,

        [switch] $Verbatim
    )

    if ($Verbatim) {
        return '@"' + $Value.Replace('"', '""') + '"'
    }

    # JSON string escapes are valid C# escapes except for the optional escaped slash.
    return (ConvertTo-Json -InputObject $Value -Compress).Replace('\/', '/')
}

function Set-CSharpConstant {
    param (
        [Parameter(Mandatory)]
        [string] $Content,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    $stringLiteralPattern = '@"(?:""|[^"])*"|"(?:\\.|[^"\\\r\n])*"'
    $pattern = '(?m)^(?<prefix>[ \t]*public const string ' +
        [regex]::Escape($Name) +
        '[ \t]*=[ \t]*)(?<literal>(?:' +
        $stringLiteralPattern +
        '))(?<suffix>[ \t]*;[ \t]*\r?)$'
    $matches = [regex]::Matches($Content, $pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' constant in $sourcePath, found $($matches.Count)."
    }

    $literal = $matches[0].Groups["literal"]
    $replacement = ConvertTo-CSharpStringLiteral `
        -Value $Value `
        -Verbatim:$literal.Value.StartsWith('@"')
    return $Content.Remove($literal.Index, $literal.Length).Insert($literal.Index, $replacement)
}

[xml]$manifest = [IO.File]::ReadAllText($manifestPath)
if ($manifest.DocumentElement.NamespaceURI -ne $manifestNamespace) {
    throw "Expected VSIX manifest namespace '$manifestNamespace' in $manifestPath."
}

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace("vs", $manifestNamespace)
$mappings = [ordered]@{
    Id = @("Id", "/vs:PackageManifest/vs:Metadata/vs:Identity/@Id")
    Name = @("DisplayName", "/vs:PackageManifest/vs:Metadata/vs:DisplayName")
    Description = @("Description", "/vs:PackageManifest/vs:Metadata/vs:Description")
    Language = @("Language", "/vs:PackageManifest/vs:Metadata/vs:Identity/@Language")
    Version = @("Version", "/vs:PackageManifest/vs:Metadata/vs:Identity/@Version")
    Author = @("Publisher", "/vs:PackageManifest/vs:Metadata/vs:Identity/@Publisher")
    Tags = @("Tags", "/vs:PackageManifest/vs:Metadata/vs:Tags")
}

$values = @{}
foreach ($constant in $mappings.Keys) {
    $input, $xpath = $mappings[$constant]
    $values[$constant] = Get-SingleManifestValue -Name $input -XPath $xpath
}

$source = [IO.File]::ReadAllText($sourcePath)
$generated = $source
foreach ($constant in $mappings.Keys) {
    $generated = Set-CSharpConstant `
        -Content $generated `
        -Name $constant `
        -Value $values[$constant]
}

if ($generated -eq $source) {
    Write-Host "VSIX generated metadata is synchronized."
    return
}

if ($Check) {
    throw "$sourcePath is not synchronized with $manifestPath."
}

[IO.File]::WriteAllText($sourcePath, $generated)
Write-Host "Synchronized VSIX generated metadata: $sourcePath"
