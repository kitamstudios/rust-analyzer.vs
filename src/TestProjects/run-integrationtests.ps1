#Requires -PSEdition Core
#Requires -Version 7.1

param (
  $SrcDir = (Join-Path $PSScriptRoot "workspace_with_tests")
  , $TestAdapterLocation = (Join-Path $PSScriptRoot "..\..\_built")
  , $VSTestPath
  , [ValidateRange(1, 99)] $VisualStudioMajorVersion = 17
)

$visualStudioModule = Join-Path $PSScriptRoot "..\..\.github\scripts\VisualStudio.psm1"
Import-Module $visualStudioModule -Force
if (-not $VSTestPath) {
  $VSTestPath = Get-VisualStudioTool -Name VSTest -MajorVersion $VisualStudioMajorVersion
}

$TcTemplateDir = Join-Path $PSScriptRoot "integrationtests"
$SrcDir = (Resolve-Path -LiteralPath $SrcDir).Path
$TestAdapterLocation = (Resolve-Path -LiteralPath $TestAdapterLocation).Path
$targetDir = Join-Path $SrcDir "target"
$tcDir = Join-Path $targetDir "debug"
New-Item -ItemType Directory -Path $tcDir -Force | Out-Null
$testResults = Join-Path $SrcDir "TestResults"
New-Item -ItemType Directory -Path $testResults -Force | Out-Null

$testContainerTemplates = @(Get-ChildItem -LiteralPath $TcTemplateDir -Recurse -Filter *.rusttests)
if ($testContainerTemplates.Count -eq 0) {
  throw "No .rusttests templates were found under '$TcTemplateDir'."
}

$testContainers = @($testContainerTemplates | ForEach-Object {
  $tcPath = Join-Path $tcDir $_.Name
  $tcJson = [System.IO.File]::ReadAllText($_).Replace("|ROOT|", "$SrcDir".Replace("\", "\\"))
  [System.IO.File]::WriteAllText($tcPath, $tcJson)

  Write-Host -ForegroundColor Blue "TC: $tcPath"
  Write-Host -ForegroundColor Blue "Contents: $([System.IO.File]::ReadAllText($tcPath))"
  Write-Host ""
  $tcPath
})

$trx = Join-Path $testResults "TestResults.trx"
if (Test-Path -LiteralPath $trx) {
  Remove-Item -LiteralPath $trx -Force
}

$vstestArguments = @(
  $testContainers
  "/TestAdapterPath:$TestAdapterLocation"
  "/Parallel"
  "/logger:console;verbosity=detailed"
  "/logger:trx;LogFileName=$trx"
)
$vstestExitCode = Invoke-VSTestProcess -VSTestPath $VSTestPath -Arguments $vstestArguments
if ($vstestExitCode -notin 0, 1) {
  throw "VSTest failed with infrastructure exit code $vstestExitCode."
}

if (-not (Test-Path -LiteralPath $trx -PathType Leaf)) {
  throw "VSTest exited with code $vstestExitCode but did not produce TRX results at '$trx'."
}

try {
  [xml] $xml = [System.IO.File]::ReadAllText($trx)
}
catch {
  throw "VSTest produced invalid TRX XML at '$trx': $($_.Exception.Message)"
}

$testRunNode = $xml.SelectSingleNode("/*[local-name()='TestRun']")
if (-not $testRunNode) {
  throw "TRX '$trx' does not contain a TestRun root element."
}

$resultsNode = $xml.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='Results']")
if (-not $resultsNode) {
  throw "TRX '$trx' does not contain a Results element. VSTest exit code: $vstestExitCode."
}

$resultNodes = @($resultsNode.SelectNodes("./*[local-name()='UnitTestResult']"))
if ($resultNodes.Count -eq 0) {
  throw "TRX '$trx' contains no UnitTestResult entries. Verify test-adapter discovery. VSTest exit code: $vstestExitCode."
}

$obtainedFile = Join-Path $testResults "obtained.txt"
$obtainedLines = @($resultNodes |
  Sort-Object { $_.GetAttribute("testName") } |
  ForEach-Object {
    $messageNode = $_.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
    $message = if ($messageNode) { $messageNode.InnerText } else { "" }
    $message = [regex]::Replace(
      $message,
      "(?m)^(thread '[^']+') \(\d+\) panicked at ",
      '$1 panicked at ')
    "[$($_.GetAttribute("outcome"))] $($_.GetAttribute("testName")) $message"
  })
[System.IO.File]::WriteAllLines($obtainedFile, [string[]] $obtainedLines)

$approvedFile = Join-Path $SrcDir "integrationtests.approved.txt"
if (-not (Test-Path -LiteralPath $approvedFile -PathType Leaf)) {
  throw "Approved integration baseline not found: '$approvedFile'."
}

$expected = @([System.IO.File]::ReadAllLines($approvedFile) | ForEach-Object { $_.TrimEnd() })
$obtained = @([System.IO.File]::ReadAllLines($obtainedFile) | ForEach-Object { $_.TrimEnd() })
$diff = @(Compare-Object $expected $obtained -CaseSensitive)
if ($diff.Count -gt 0) {
  $diff | Format-Table
  throw "Test failed. See above for the diff."
}

Write-Host "Standalone acceptance harness matched $($resultNodes.Count) test result(s). VSTest exit code: $vstestExitCode."
