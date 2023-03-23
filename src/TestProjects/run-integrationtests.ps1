#Requires -PSEdition Core
#Requires -Version 7.1

param (
  $SrcDir = (Join-Path $PSScriptRoot "workspace_with_tests")
  , $TestAdapterLocation
)

$SrcDir = Resolve-Path $SrcDir
$SrcName = Split-Path $SrcDir
$targetDir = Join-Path $SrcDir "target"
$tcDir = Join-Path $targetDir "debug"
mkdir -Force $tcDir | Out-Null
$testResults = Join-Path $SrcDir "TestResults"
mkdir -Force $testResults | Out-Null

$testContainers = dir $SrcDir -Recurse -Filter Cargo.toml | % {
  $tcName = ([System.IO.Path]::GetRelativePath((Split-Path $SrcDir), (Split-Path $_)))
  $tcName = $tcName.Split([IO.Path]::GetInvalidFileNameChars()) -join '_'
  $tcPath = Join-Path $tcDir "$tcName.rusttests"
  $tc = @{
    ThisPath = $tcPath
    Manifest = $_.FullName
    TargetDir = $targetDir
    AdditionalTestDiscoveryArguments = ""
    AdditionalTestExecutionArguments = ""
    TestExecutionEnvironment = ""
    Profile = "dev"
    TestExe = "<not_yet_generated>"
  }
  $tcJson = ConvertTo-Json $tc
  $tcJson >$tcPath
  Write-Host -ForegroundColor Blue "TC: $tcPath"
  Write-Host -ForegroundColor Blue "Contents: $(gc $tcPath)"
  Write-Host ""
  $tcPath
}

$trx = Join-Path $testResults "TestResults.trx"
vstest.console.exe @testContainers /TestAdapterPath:"$TestAdapterLocation" /Parallel "/logger:console;verbosity=detailed" "/logger:trx;LogFileName=$trx"
cmd /c echo "Clear up the vstest.console.exe error..."

$obtainedFile = Join-Path $testResults "obtained.txt"
$xml = [xml] (gc $trx)
($xml.TestRun.Results.UnitTestResult | Sort-Object -Property testName | % { "[$($_.outcome)] $($_.testName) $($_.Output.ErrorInfo.Message)" }) >$obtainedFile
$expected = gc (Join-Path $SrcDir "integrationtests.approved.txt")
$obtained = gc $obtainedFile

$diff = Compare-Object $expected $obtained -CaseSensitive
if ($diff.Length)
{
  $diff | Format-Table
  throw "Test failed. See above for the diff."
}
