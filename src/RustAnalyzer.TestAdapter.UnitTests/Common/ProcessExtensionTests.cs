using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Extensions;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

[Trait("type", "IntegrationTests")]
public sealed class ProcessExtensionTests
{
    private const int TimeoutSeconds = 15;

    [Fact]
    public async Task CanFindDeadParentProcessIdAsync()
    {
        var p1 = Process.Start(("cmd", $"/c start /MIN TiMeOUT {TimeoutSeconds} /NOBREAK").PSI());
        await Task.Delay(1.Seconds());

        var procs = "timeout".GetProcessesByName();
        var ppids = procs.Select(p => p.GetParentProcessId());

        procs.Should().NotBeEmpty();
        ppids.Should().NotBeEmpty();
        p1.HasExited.Should().BeTrue();
        ppids.Should().Contain(p1.Id);
    }

    [Fact]
    public void TestProcessOwnerUser()
    {
        var p1 = Process.Start(("cmd", $"/c timeout.exe {TimeoutSeconds} /NOBREAK").PSI());

        p1.GetProcessOwnerUser()
            .Should().Be(Process.GetCurrentProcess().GetProcessOwnerUser());
    }

    [Fact]
    public async Task TestGetOrphanedProcessesAsync()
    {
        Process.Start(("cmd.exe", $"/c timeout {TimeoutSeconds} /NOBREAK").PSI());
        var pDeadParent = Process.Start(("cmd.exe", $"/c start /MIN timeout {TimeoutSeconds} /NOBREAK").PSI());
        await Task.Delay(1.Seconds());

        // NOTE: Not tested for dead parent PID reuse case.
        "timeout".GetOrphanedProcesses()
            .Select(x => x.GetParentProcessId())
            .Should().Contain(pDeadParent.Id);
    }

    [Fact]
    public async Task TestKillSafeAsync()
    {
        var proc = Process.Start(("cmd.exe", $"/c timeout 0").PSI());
        await Task.Delay(1000.Milliseconds());
        var act = () => proc.KillSafe();

        proc.HasExited.Should().BeTrue();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RunWithLoggingUsesStructuredProcessCategoryAndLegacyCompatibilityAsync()
    {
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var logger = factory.CreateLogger(typeof(ProcessRunner).FullName);

        using var process = await ProcessRunner.RunWithLogging(
            "cmd.exe",
            new[] { "/c", "exit", "0" },
            Environment.CurrentDirectory,
            new Dictionary<string, string>(),
            CancellationToken.None,
            logger);

        var entries = provider.Entries.ToArray();
        entries.Select(entry => entry.EventId).Should().Equal(
            new EventId(1, "ProcessStarted"),
            new EventId(2, "ProcessFinished"));
        entries.Should().OnlyContain(
            entry => entry.Category == typeof(ProcessRunner).FullName
                && entry.Level == LogLevel.Information
                && entry.Exception == null);
        entries[0].Template.Should().Be(
            "Started PID:{ProcessId} with args: {Arguments}...");
        entries[0].Properties["ProcessId"].Should().Be(process.ProcessId);
        entries[0].Properties["Arguments"].Should().Be(process.Arguments);
        entries[1].Template.Should().Be(
            "... Finished PID {ProcessId} with exit code {ExitCode}.");
        entries[1].Properties["ProcessId"].Should().Be(process.ProcessId);
        entries[1].Properties["ExitCode"].Should().Be(0);

        var legacyLogger = new RecordingLegacyLogger();
        using var legacyProcess = await ProcessRunner.RunWithLogging(
            "cmd.exe",
            new[] { "/c", "exit", "0" },
            Environment.CurrentDirectory,
            new Dictionary<string, string>(),
            CancellationToken.None,
            legacyLogger);

        legacyLogger.Messages.Should().HaveCount(2);
        provider.Entries.Should().HaveCount(2);
    }

    private sealed class RecordingLegacyLogger :
        KS.RustAnalyzer.TestAdapter.Common.ILogger
    {
        public List<string> Messages { get; } = new();

        public void WriteLine(string format, params object[] args)
        {
            Messages.Add(string.Format(format, args));
        }

        public void WriteError(string format, params object[] args)
        {
            Messages.Add(string.Format(format, args));
        }
    }
}

public static class Extensions
{
    public static ProcessStartInfo PSI(this (string Name, string Args) startInfo)
    {
        return new ProcessStartInfo { FileName = startInfo.Name, Arguments = startInfo.Args, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
    }
}
