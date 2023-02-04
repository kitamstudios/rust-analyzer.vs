using System.IO;
using System.Linq;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class ManifestTests
{
    [Fact]
    public void SimplestExe()
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, @"hello_world\Cargo.toml");
        var cargo = Manifest.Create(cmPath);

        cargo.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = Path.GetDirectoryName(cmPath),
                FullPath = cmPath,
            });
        cargo.Targets.Single().Should().BeEquivalentTo(
            new
            {
                TargetFileName = "hello_world.exe",
            });
    }

    [Fact]
    public void SimplestLib()
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, @"hello_library\Cargo.toml");
        var cargo = Manifest.Create(cmPath);

        cargo.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = Path.GetDirectoryName(cmPath),
                FullPath = cmPath,
            });
        cargo.Targets.Single().Should().BeEquivalentTo(
            new
            {
                TargetFileName = "libhello_lib.rlib",
            });
    }

    [Theory]
    [InlineData(@"hello_world\Cargo.toml", true, @"hello_world.exe")]
    [InlineData(@"hello_library\Cargo.toml", false, @"libhello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", true, @"main.exe")]
    [InlineData(@"hello_workspace\shared\Cargo.toml", false, @"libshared.rlib")]
    public void TargetFileNameTests(string cargoRelPath, bool isRunnable, string targetFileName)
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, cargoRelPath);
        var cargo = Manifest.Create(cmPath);

        cargo.Targets.Single().Should().BeEquivalentTo(
            new
            {
                IsRunnable = isRunnable,
                TargetFileName = targetFileName,
            });
    }

    [Theory]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\target\debug\libhello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\main.exe")]
    [InlineData(@"hello_workspace\subfolder\shared2\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\libshared2.rlib")]
    public void WorkspaceRootTests(string cargoRelPath, string workspaceRelPath, string targetFileRelPath)
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, cargoRelPath);
        var cargo = Manifest.Create(cmPath);

        cargo.WorkspaceRoot.Should().Be(Path.Combine(TestHelpers.ThisTestRoot, workspaceRelPath));
        cargo.Targets.Single().GetPath("dev").Should().Be(Path.Combine(TestHelpers.ThisTestRoot, targetFileRelPath));
    }

    [Theory]
    [InlineData(
        @"hello_world\Cargo.toml",
        @"hello_world\src\main.rs",
        @"..\target\debug\hello_world.exe")]
    [InlineData(
        @"hello_world\Cargo.toml",
        @"hello_world\Cargo.toml",
        @"target\debug\hello_world.exe")]
    public void GetTargetPathForProfileRelativeToPathTests(string manifestPath, string filePath, string ret)
    {
        var cargo = Manifest.Create(Path.Combine(TestHelpers.ThisTestRoot, manifestPath));

        cargo.Targets.Single().GetPathRelativeTo("dev", Path.Combine(TestHelpers.ThisTestRoot, filePath)).Should().Be(ret);
    }
}
