using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Extensions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

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
}

public static class Extensions
{
    public static ProcessStartInfo PSI(this (string Name, string Args) startInfo)
    {
        return new ProcessStartInfo { FileName = startInfo.Name, Arguments = startInfo.Args, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
    }
}
