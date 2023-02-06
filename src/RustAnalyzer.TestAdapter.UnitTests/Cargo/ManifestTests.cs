using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class ManifestTests
{
    [Fact]
    public async Task SimplestWorkspaceAsync()
    {
        var cmPath = TestHelpers.ThisTestRoot2.Combine((PathEx)@"hello_workspace\Cargo.toml");
        var wkRoot = TestHelpers.ThisTestRoot2.Combine((PathEx)@"hello_workspace");
        var package = await wkRoot.MS().GetPackageAsync(cmPath, default);

        package.Parent.WorkspaceRoot.Should().Be(wkRoot);
        package.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = cmPath.GetDirectoryName(),
                FullPath = cmPath,
            });

        package.GetTargets().Should().BeEmpty();
    }

    [Fact]
    public async Task SimplestExeAsync()
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, @"hello_world\Cargo.toml");
        string wkRoot = Path.Combine(TestHelpers.ThisTestRoot, @"hello_world");
        var cargo = Manifest.Create(cmPath, wkRoot);

        cargo.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = Path.GetDirectoryName(cmPath),
                FullPath = cmPath,
            });
        (await cargo.GetTargets()).Single().Should().BeEquivalentTo(
            new
            {
                TargetFileName = "hello_world.exe",
            });
    }

    [Fact]
    public async Task SimplestExe2Async()
    {
        var cmPath = TestHelpers.ThisTestRoot2.Combine((PathEx)@"hello_world\Cargo.toml");
        var wkRoot = TestHelpers.ThisTestRoot2.Combine((PathEx)@"hello_world");
        var package = await new MetadataService(new CargoService(TestHelpers.TL.T, TestHelpers.TL.L), wkRoot, TestHelpers.TL).GetPackageAsync(cmPath, default);

        package.Parent.WorkspaceRoot.Should().Be(wkRoot);
        package.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = cmPath.GetDirectoryName(),
                FullPath = cmPath,
            });

        package.GetTargets().Single().Should().BeEquivalentTo(
            new
            {
                TargetFileName = (PathEx)"hello_world.exe",
            });
    }

    [Fact]
    public async Task SimplestLibAsync()
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, @"hello_library\Cargo.toml");
        string wkRoot = Path.Combine(TestHelpers.ThisTestRoot, @"hello_library");
        var cargo = Manifest.Create(cmPath, wkRoot);

        cargo.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = Path.GetDirectoryName(cmPath),
                FullPath = cmPath,
            });
        (await cargo.GetTargets()).Single().Should().BeEquivalentTo(
            new
            {
                TargetFileName = "libhello_lib.rlib",
            });
    }

    [Fact]
    public async Task SimplestLib2Async()
    {
        var cmPath = (PathEx)Path.Combine(TestHelpers.ThisTestRoot, @"hello_library\Cargo.toml");
        var wkRoot = (PathEx)Path.Combine(TestHelpers.ThisTestRoot, @"hello_library");
        var package = await new MetadataService(new CargoService(TestHelpers.TL.T, TestHelpers.TL.L), wkRoot, TestHelpers.TL).GetPackageAsync(cmPath, default);

        package.Parent.WorkspaceRoot.Should().Be(wkRoot);
        package.Should().BeEquivalentTo(
            new
            {
                WorkspaceRoot = cmPath.GetDirectoryName(),
                FullPath = cmPath,
            });
        package.GetTargets().Single().Should().BeEquivalentTo(
            new
            {
                TargetFileName = (PathEx)"libhello_lib.rlib",
            });
    }

    [Theory]
    [InlineData(@"hello_world\Cargo.toml", "hello_world", true, @"hello_world.exe")]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", false, @"libhello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", true, @"main.exe")]
    [InlineData(@"hello_workspace\shared\Cargo.toml", "hello_workspace", false, @"libshared.rlib")]
    public async Task TargetFileNameTestsAsync(string cargoRelPath, string workspaceRootRel, bool isRunnable, string targetFileName)
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, cargoRelPath);
        string wkRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootRel);
        var cargo = Manifest.Create(cmPath, wkRoot);

        (await cargo.GetTargets()).Single().Should().BeEquivalentTo(
            new
            {
                IsRunnable = isRunnable,
                TargetFileName = targetFileName,
            });
    }

    [Theory]
    [InlineData(@"hello_world\Cargo.toml", "hello_world", true, @"hello_world.exe")]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", false, @"libhello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", true, @"main.exe")]
    [InlineData(@"hello_workspace\shared\Cargo.toml", "hello_workspace", false, @"libshared.rlib")]
    public async Task TargetFileNameTests2Async(string cargoRelPath, string workspaceRootRel, bool isRunnable, string targetFileName)
    {
        var cmPath = TestHelpers.ThisTestRoot2.Combine((PathEx)cargoRelPath);
        var wkRoot = TestHelpers.ThisTestRoot2.Combine((PathEx)workspaceRootRel);
        var package = await wkRoot.MS().GetPackageAsync(cmPath, default);

        package.GetTargets().Single().Should().BeEquivalentTo(
            new
            {
                IsRunnable = isRunnable,
                TargetFileName = (PathEx)targetFileName,
            });
    }

    [Theory]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\target\debug\libhello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\main.exe")]
    [InlineData(@"hello_workspace\subfolder\shared2\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\libshared2.rlib")]
    public async Task WorkspaceRootTestsAsync(string cargoRelPath, string workspaceRelPath, string targetFileRelPath)
    {
        string cmPath = Path.Combine(TestHelpers.ThisTestRoot, cargoRelPath);
        string wkRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRelPath);
        var cargo = Manifest.Create(cmPath, wkRoot);

        cargo.WorkspaceRoot.Should().Be(wkRoot);
        (await cargo.GetTargets()).Single().GetPath("dev").Should().Be(Path.Combine(TestHelpers.ThisTestRoot, targetFileRelPath));
    }

    [Theory]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\target\debug\libhello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\main.exe")]
    [InlineData(@"hello_workspace\subfolder\shared2\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\libshared2.rlib")]
    public async Task WorkspaceRootTests2Async(string cargoRelPath, string workspaceRelPath, string targetFileRelPath)
    {
        var cmPath = TestHelpers.ThisTestRoot2.Combine((PathEx)cargoRelPath);
        var wkRoot = TestHelpers.ThisTestRoot2.Combine((PathEx)workspaceRelPath);
        var package = await wkRoot.MS().GetPackageAsync(cmPath, default);

        package.WorkspaceRoot.Should().Be(wkRoot);
        package.GetTargets().Single().GetPath("dev").Should().Be(TestHelpers.ThisTestRoot2.Combine((PathEx)targetFileRelPath));
    }

    [Theory]
    [InlineData(
        @"hello_world\Cargo.toml",
        @"hello_world",
        @"hello_world\src\main.rs",
        @"..\target\debug\hello_world.exe")]
    [InlineData(
        @"hello_world\Cargo.toml",
        @"hello_world",
        @"hello_world\Cargo.toml",
        @"target\debug\hello_world.exe")]
    public async Task GetTargetPathForProfileRelativeToPathTestsAsync(string manifestPathRel, string workspaceRootRel, string filePath, string ret)
    {
        var manifestPath = Path.Combine(TestHelpers.ThisTestRoot, manifestPathRel);
        var wkRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootRel);
        var cargo = Manifest.Create(manifestPath, wkRoot);

        (await cargo.GetTargets()).Single().GetPathRelativeTo("dev", Path.Combine(TestHelpers.ThisTestRoot, filePath)).Should().Be(ret);
    }

    [Theory]
    [InlineData(
        @"hello_world\Cargo.toml",
        @"hello_world",
        @"hello_world\src\main.rs",
        @"..\target\debug\hello_world.exe")]
    [InlineData(
        @"hello_world\Cargo.toml",
        @"hello_world",
        @"hello_world\Cargo.toml",
        @"target\debug\hello_world.exe")]
    public async Task GetTargetPathForProfileRelativeToPathTests2Async(string manifestPathRel, string workspaceRootRel, string filePath, string ret)
    {
        var manifestPath = TestHelpers.ThisTestRoot2.Combine((PathEx)manifestPathRel);
        var wkRoot = TestHelpers.ThisTestRoot2.Combine((PathEx)workspaceRootRel);
        var package = await wkRoot.MS().GetPackageAsync(manifestPath, default);

        var relPath = package.GetTargets().Single().GetPathRelativeTo("dev", TestHelpers.ThisTestRoot2.Combine((PathEx)filePath));

        relPath.Should().Be((PathEx)ret);
    }
}
