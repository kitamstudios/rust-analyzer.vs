namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

public sealed class ToolchainServiceExtensionsTests
{
    [Fact]
    public async Task TestGetActiveToolChainAsync()
    {
        (await ToolchainServiceExtensions.GetDefaultToolchainAsync(TestHelpers.ThisTestRoot, default))
            .Should()
            .EndWith($"-{ToolchainServiceExtensions.AlwaysAvailableTarget}");
    }

    [Fact]
    public async Task TestGetBinAndLibPathsAsync()
    {
        var (binPath, libPath) = await ToolchainServiceExtensions.GetBinAndLibPathsAsync(TestHelpers.ThisTestRoot, default);

        Directory.EnumerateFiles(libPath, "std-*.*")
            .Select(x => Path.GetFileName(x).Remove(3, "-cef76c2685dfb4ca".Length))
            .Should()
            .BeEquivalentTo(new[] { "std.dll", "std.pdb", "std.dll.lib" });

        Directory.EnumerateFiles(binPath, "std-*.*")
            .Select(x => Path.GetFileName(x).Remove(3, "-cef76c2685dfb4ca".Length))
            .Should()
            .BeEquivalentTo(new[] { "std.dll", "std.pdb" });

        (binPath + "rustc.exe").FileExists().Should().BeTrue();
        (binPath + Constants.CargoExe).FileExists().Should().BeTrue();
    }

    [Fact]
    public async Task TestGetInstalledToolchainsBasicAsync()
    {
        var installToolchains = await ToolchainServiceExtensions.GetInstalledToolchainsAsync(TestHelpers.ThisTestRoot, default);

        installToolchains.Should().NotBeEmpty();
        installToolchains.Select(x => x.Name).Should().Contain(x => !x.IsNullOrEmptyOrWhiteSpace());
        installToolchains.Select(x => x.Version).Should().Contain(x => !x.IsNullOrEmptyOrWhiteSpace());
        installToolchains.Where(x => x.IsDefault).Should().HaveCount(1);
    }

    [Fact]
    public async Task TestGetTargetsAsync()
    {
        var targets = await ToolchainServiceExtensions.GetTargets(default);

        targets.Should().NotContain(ToolchainServiceExtensions.AlwaysAvailableTarget);
        targets.Should().OnlyContain(t => !t.Contains(" ("));
        targets.Take(ToolchainServiceExtensions.CommonTargets.Length)
            .Should()
            .ContainInOrder(ToolchainServiceExtensions.CommonTargets.OrderBy(x => x));
    }
}
