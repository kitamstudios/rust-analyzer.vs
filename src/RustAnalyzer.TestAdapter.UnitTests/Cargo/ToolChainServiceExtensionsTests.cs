namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

public sealed class ToolChainServiceExtensionsTests
{
    [Fact]
    public async Task TestGetActiveToolChainAsync()
    {
        (await ToolChainServiceExtensions.GetDefaultToolchainAsync(TestHelpers.ThisTestRoot, default))
            .Should()
            .EndWith("-x86_64-pc-windows-msvc");
    }

    [Fact]
    public async Task TestGetBinAndLibPathsAsync()
    {
        var (binPath, libPath) = await ToolChainServiceExtensions.GetBinAndLibPathsAsync(TestHelpers.ThisTestRoot, default);

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
        var installToolchains = await ToolChainServiceExtensions.GetInstalledToolchainsAsync(TestHelpers.ThisTestRoot, default);

        installToolchains.Should().NotBeEmpty();
        installToolchains.Select(x => x.Name).Should().Contain(x => !x.IsNullOrEmptyOrWhiteSpace());
        installToolchains.Select(x => x.Version).Should().Contain(x => !x.IsNullOrEmptyOrWhiteSpace());
        installToolchains.Where(x => x.IsDefault).Should().HaveCount(1);
    }
}
