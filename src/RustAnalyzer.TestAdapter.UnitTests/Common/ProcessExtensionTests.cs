using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

public class ProcessExtensionTests
{
    [Fact]
    public void FindAliveParentProcessId()
    {
        var p1 = Process.Start(CreatePSI("cmd.exe", "/c timeout 5"));

        var ppids = Process.GetProcessesByName("timeout").Select(p => p.Id.GetParentProcessId());
        ppids.Should().OnlyContain(x => x == p1.Id);

        p1.Kill();
    }

    [Fact]
    public void CannotFindDeadParentProcessId()
    {
        var p1 = Process.Start(CreatePSI("cmd.exe", "/c timeout 5"));
        p1.Kill();

        var ppids = Process.GetProcessesByName("timeout").Select(p => p.Id.GetParentProcessId());
        ppids.Should().NotContain(x => x == p1.Id);
    }

    [Fact]
    public void TestProcessOwnerUer()
    {
        var p1 = Process.Start(CreatePSI("cmd.exe", "/c timeout 5"));
        p1.GetProcessOwnerUser().Should().Be(Process.GetCurrentProcess().GetProcessOwnerUser());
        p1.Kill();
    }

    private static ProcessStartInfo CreatePSI(string name, string args)
    {
        return new ProcessStartInfo { FileName = name, Arguments = args, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
    }
}
