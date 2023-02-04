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

    [Theory]
    [InlineData(@"not_a_project\src\main.rs", "not_a_project", @"not_a_project\Cargo.toml", false)]
    [InlineData(@"not_a_project\src", "not_a_project", @"not_a_project\Cargo.toml", false)]
    [InlineData(@"hello_library\src\lib.rs", "hello_library", @"hello_library\Cargo.toml", true)]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\Cargo.toml", true)]
    [InlineData(@"hello_workspace\main\src\main.rs", "hello_workspace", @"hello_workspace\main\Cargo.toml", true)]
    [InlineData(@"hello_workspace\main\src", "hello_workspace", @"hello_workspace\main\Cargo.toml", true)]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\main\Cargo.toml", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg1.rs", "workspace_with_example", @"workspace_with_example\lib\Cargo.toml", true)]
    [InlineData(@"c:\workspace_with_example\lib\examples\eg1.rs", "workspace_with_example", null, false)]
    public void GetContainingManifestOrThisTests(string fileOrFolder, string workspaceRootx, string parentCargoRelPath, bool foundParentManifest)
    {
        string path = Path.Combine(TestHelpers.ThisTestRoot, fileOrFolder);
        var workspaceRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootx);
        var found = Manifest.TryGetParentManifestOrThisUnderWorkspace(workspaceRoot, path, out string parentCargoPath);

        found.Should().Be(foundParentManifest);
        var expectedParentManifestpath = found ? Path.Combine(TestHelpers.ThisTestRoot, parentCargoRelPath) : null;
        parentCargoPath.Should().Be(expectedParentManifestpath);
    }
}
