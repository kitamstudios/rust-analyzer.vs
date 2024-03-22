#Requires -PSEdition Core
#Requires -Version 7.1

param (
  $SrcDir = (Join-Path $PSScriptRoot "workspace_with_tests")
  , $TestAdapterLocation
)

$TcTemplateDir = Join-Path $PSScriptRoot "integrationtests"
$SrcDir = Resolve-Path $SrcDir
$SrcName = Split-Path $SrcDir
$targetDir = Join-Path $SrcDir "target"
$tcDir = Join-Path $targetDir "debug"
mkdir -Force $tcDir | Out-Null
$testResults = Join-Path $SrcDir "TestResults"
mkdir -Force $testResults | Out-Null

$testContainers = dir $TcTemplateDir -Recurse -Filter *.rusttests | % {
  $tcPath = Join-Path $tcDir $_.Name
  $tcJson = [System.IO.File]::ReadAllText($_).Replace("|ROOT|", "$SrcDir".Replace("\", "\\"))
  [System.IO.File]::WriteAllText($tcPath, $tcJson)

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
$expected = gc (Join-Path $SrcDir "integrationtests.approved.txt") | % { $_.TrimEnd() }
$obtained = gc $obtainedFile | % { $_.TrimEnd() }

$diff = Compare-Object $expected $obtained -CaseSensitive
if ($diff.Length)
{
  $diff | Format-Table
  throw "Test failed. See above for the diff."
}
