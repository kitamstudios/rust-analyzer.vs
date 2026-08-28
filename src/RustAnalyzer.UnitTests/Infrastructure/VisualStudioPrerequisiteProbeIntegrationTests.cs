using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "IntegrationTests")]
public sealed class VisualStudioPrerequisiteProbeIntegrationTests
{
    private const string RustupAutoInstall = "RUSTUP_AUTO_INSTALL";

    [Fact]
    public void FindsExecutableOnlyInProvidedProcessPath()
    {
        var expected = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var probe = new VisualStudioPrerequisiteProbe(
            $"{Path.Combine(Environment.SystemDirectory, "missing")}{Path.PathSeparator}\"{Environment.SystemDirectory}\"");

        probe.FindExecutable("cmd.exe").Should().Be(expected);
        probe.FindExecutable("prerequisite-probe-does-not-exist.exe").Should().BeNull();
        new VisualStudioPrerequisiteProbe(string.Empty).FindExecutable("cmd.exe").Should().BeNull();
    }

    [Fact]
    public async Task RunsExecutableAndCapturesOutputAsync()
    {
        var probe = new VisualStudioPrerequisiteProbe(Environment.SystemDirectory);
        var command = probe.FindExecutable("cmd.exe");

        var result = await probe.RunAsync(
            command,
            new[] { "/d", "/s", "/c", "echo prerequisite-probe-ok" },
            default);

        result.WasStarted.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Trim().Should().Be("prerequisite-probe-ok");
        result.StandardError.Should().BeEmpty();
        result.StartError.Should().BeEmpty();
    }

    [Fact]
    public async Task CapturesNonzeroProcessExitAsync()
    {
        var probe = new VisualStudioPrerequisiteProbe(Environment.SystemDirectory);
        var command = probe.FindExecutable("cmd.exe");

        var result = await probe.RunAsync(
            command,
            new[] { "/d", "/s", "/c", "exit 23" },
            default);

        result.WasStarted.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.ExitCode.Should().Be(23);
    }

    [Fact]
    public async Task ForcesRustupAutoInstallOffOnlyInChildProcessAsync()
    {
        var parentValue = Environment.GetEnvironmentVariable(RustupAutoInstall);
        var probe = new VisualStudioPrerequisiteProbe(Environment.SystemDirectory);
        var command = probe.FindExecutable("cmd.exe");

        var result = await probe.RunAsync(
            command,
            new[] { "/d", "/s", "/c", $"echo [%{RustupAutoInstall}%]" },
            default);

        result.IsSuccess.Should().BeTrue();
        result.StandardOutput.Trim().Should().Be("[0]");
        Environment.GetEnvironmentVariable(RustupAutoInstall).Should().Be(parentValue);
    }

    [Fact]
    public async Task ForcesRustupAutoInstallOffWhenIsolatedParentHasItEnabledAsync()
    {
        var xunitParentValue = Environment.GetEnvironmentVariable(RustupAutoInstall);
        var assemblyPath = typeof(VisualStudioPrerequisiteProbe).Assembly.Location;
        var assemblyPathLiteral = assemblyPath.Replace("'", "''");
        var processRunnerAssemblyPathLiteral = typeof(ProcessRunner).Assembly.Location.Replace("'", "''");
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    $parentBefore = [Environment]::GetEnvironmentVariable('{RustupAutoInstall}')
    if (-not [string]::Equals($parentBefore, '1', [StringComparison]::Ordinal)) {{
        throw ""Expected helper {RustupAutoInstall} to be '1' before probing, but found '$parentBefore'.""
    }}

    [void][Reflection.Assembly]::LoadFrom('{processRunnerAssemblyPathLiteral}')
    $probeAssembly = [Reflection.Assembly]::LoadFrom('{assemblyPathLiteral}')
    $probeType = $probeAssembly.GetType('KS.RustAnalyzer.Infrastructure.VisualStudioPrerequisiteProbe', $true)
    $constructor = $probeType.GetConstructor([type[]]@([string]))
    $probe = $constructor.Invoke([object[]]@([Environment]::SystemDirectory))
    $command = $probeType.GetMethod('FindExecutable').Invoke($probe, [object[]]@('cmd.exe'))
    if ([string]::IsNullOrWhiteSpace($command) -or -not [IO.File]::Exists($command)) {{
        throw ""The nested command path does not exist: '$command'.""
    }}

    $runArguments = [object[]]::new(3)
    $runArguments[0] = $command
    $runArguments[1] = [string[]]@('/d', '/s', '/c', 'echo [%{RustupAutoInstall}%]')
    $runArguments[2] = [Threading.CancellationToken]::None
    $runTask = $probeType.GetMethod('RunAsync').Invoke($probe, $runArguments)
    $result = $runTask.GetAwaiter().GetResult()
    $resultDiagnostics = ""StartError: '$($result.StartError)'; nested stderr: '$($result.StandardError.Trim())'.""
    if (-not $result.WasStarted) {{
        throw ""The production probe did not start the nested command. $resultDiagnostics""
    }}

    if (-not $result.IsSuccess) {{
        throw ""The production probe did not report success. Exit code: '$($result.ExitCode)'. $resultDiagnostics""
    }}

    if ($result.ExitCode -ne 0) {{
        throw ""Expected nested exit code 0, but found '$($result.ExitCode)'. $resultDiagnostics""
    }}

    $nestedOutput = $result.StandardOutput.Trim()
    if (-not [string]::Equals($nestedOutput, '[0]', [StringComparison]::Ordinal)) {{
        throw ""Expected nested stdout '[0]', but found '$nestedOutput'. $resultDiagnostics""
    }}

    $parentAfter = [Environment]::GetEnvironmentVariable('{RustupAutoInstall}')
    if (-not [string]::Equals($parentAfter, '1', [StringComparison]::Ordinal)) {{
        throw ""Expected helper {RustupAutoInstall} to remain '1', but found '$parentAfter'. $resultDiagnostics""
    }}
}}
catch {{
    [Console]::Error.WriteLine(""Prerequisite probe isolation helper failed: $($_.Exception.Message)"")
    exit 1
}}

exit 0
";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powerShell = Path.Combine(
            Environment.SystemDirectory,
            @"WindowsPowerShell\v1.0\powershell.exe");
        var helperEnvironment = new Dictionary<string, string>
        {
            [RustupAutoInstall] = "1",
        };

        using var helper = ProcessRunner.Run(
            powerShell,
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encodedScript },
            Path.GetDirectoryName(assemblyPath),
            helperEnvironment,
            default);
        var exitCode = await helper;
        var standardOutput = string.Join(Environment.NewLine, helper.StandardOutputLines);
        var standardError = string.Join(Environment.NewLine, helper.StandardErrorLines);

        exitCode.Should().Be(
            0,
            "the isolated helper should self-validate.{0}Helper stdout:{0}{1}{0}Helper stderr:{0}{2}",
            Environment.NewLine,
            standardOutput,
            standardError);
        Environment.GetEnvironmentVariable(RustupAutoInstall).Should().Be(xunitParentValue);
    }

    [Fact]
    public async Task ConvertsProcessStartFailureToProbeResultAsync()
    {
        var probe = new VisualStudioPrerequisiteProbe(Environment.SystemDirectory);
        var missingExecutable = Path.Combine(Environment.SystemDirectory, "prerequisite-probe-does-not-exist.exe");

        var result = await probe.RunAsync(missingExecutable, Array.Empty<string>(), default);

        result.WasStarted.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.ExitCode.Should().BeNull();
        result.StartError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PropagatesCancellationAcrossProcessBoundaryAsync()
    {
        var windowsPowerShellDirectory = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0");
        var probe = new VisualStudioPrerequisiteProbe(windowsPowerShellDirectory);
        var powerShell = probe.FindExecutable("powershell.exe");
        using var cancellation = new CancellationTokenSource();

        var run = probe.RunAsync(
            powerShell,
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" },
            cancellation.Token);
        cancellation.Cancel();
        Func<Task> awaitRun = async () => await run;

        await awaitRun.Should().ThrowAsync<OperationCanceledException>();
    }
}
