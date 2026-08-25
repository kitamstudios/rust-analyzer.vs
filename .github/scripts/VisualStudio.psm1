Set-StrictMode -Version Latest

function Get-VsWherePath {
    $command = Get-Command vswhere.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $installerPath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $installerPath -PathType Leaf) {
        return $installerPath
    }

    throw "vswhere.exe was not found. Install the Visual Studio Installer."
}

function Get-VisualStudioTool {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [ValidateSet("MSBuild", "VSTest")]
        [string] $Name,

        [ValidateRange(1, 99)]
        [int] $MajorVersion = 17
    )

    $vswhere = Get-VsWherePath
    $json = (& $vswhere -all -products * -format json) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        throw "vswhere.exe failed with exit code $LASTEXITCODE."
    }

    $relativePaths = switch ($Name) {
        "MSBuild" {
            @("MSBuild\Current\Bin\MSBuild.exe")
        }

        "VSTest" {
            @(
                "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                "Common7\IDE\Extensions\TestPlatform\vstest.console.exe"
            )
        }
    }

    $candidates = foreach ($instance in @($json | ConvertFrom-Json)) {
        if (-not $instance.isComplete) {
            continue
        }

        $version = [version]$instance.installationVersion
        if ($version.Major -ne $MajorVersion) {
            continue
        }

        foreach ($relativePath in $relativePaths) {
            $toolPath = Join-Path $instance.installationPath $relativePath
            if (Test-Path -LiteralPath $toolPath -PathType Leaf) {
                [pscustomobject]@{
                    Path = $toolPath
                    Version = $version
                }
            }
        }
    }

    $candidate = $candidates |
        Sort-Object -Property Version -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        throw "$Name was not found in a complete Visual Studio major $MajorVersion installation reported by vswhere.exe."
    }

    return $candidate.Path
}

function Invoke-VSTestProcess {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $VSTestPath,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $VSTestPath
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $windirKeys = @($startInfo.EnvironmentVariables.Keys | Where-Object { $_ -ieq "windir" })
    $windir = if ($windirKeys.Count -gt 0) {
        $startInfo.EnvironmentVariables[$windirKeys[0]]
    }
    else {
        $startInfo.EnvironmentVariables["SystemRoot"]
    }

    if (-not [string]::IsNullOrWhiteSpace($windir)) {
        foreach ($windirKey in $windirKeys) {
            $startInfo.EnvironmentVariables.Remove($windirKey)
        }

        $startInfo.EnvironmentVariables.Add("windir", $windir)
    }

    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $process.WaitForExit()
        return $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

Export-ModuleMember -Function Get-VisualStudioTool, Invoke-VSTestProcess
