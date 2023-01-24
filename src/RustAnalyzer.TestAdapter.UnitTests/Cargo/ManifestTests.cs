using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class ManifestTests
{
    private static readonly string ThisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().Location).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    [Fact]
    public void SimplestExe()
    {
        string cmPath = Path.Combine(ThisTestRoot, @"hello_world\Cargo.toml");
        var cargo = Manifest.Create(cmPath);

        cargo.Should().BeEquivalentTo(
            new
            {
                TargetFileNameWithoutExtension = "hello_world",
                TargetFileExtension = ".exe",
                TargetFileName = "hello_world.exe",
                WorkspaceRoot = Path.GetDirectoryName(cmPath),
                FullPath = cmPath,
            });
    }

    [Fact]
    public void SimplestLib()
    {
        string cmPath = Path.Combine(ThisTestRoot, @"hello_library\Cargo.toml");
        var cargo = Manifest.Create(cmPath);

        cargo.Should().BeEquivalentTo(
            new
            {
                TargetFileNameWithoutExtension = "hello_lib",
                TargetFileExtension = ".rlib",
                TargetFileName = "hello_lib.rlib",
                WorkspaceRoot = Path.GetDirectoryName(cmPath),
                FullPath = cmPath,
            });
    }

    [Theory]
    [InlineData(@"hello_world\Cargo.toml", @"hello_world")]
    [InlineData(@"hello_library\Cargo.toml", @"hello_lib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", @"main")]
    [InlineData(@"hello_workspace\shared\Cargo.toml", @"shared")]
    public void StartupProjectEntryNameTests(string cargoRelPath, string startupProjectEntryName)
    {
        string cmPath = Path.Combine(ThisTestRoot, cargoRelPath);
        var cargo = Manifest.Create(cmPath);

        cargo.StartupProjectEntryName.Should().Be(startupProjectEntryName);
    }

    [Theory]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\target\debug\hello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\main.exe")]
    public void WorkspaceRootTests(string cargoRelPath, string workspaceRelPath, string targetFileRelPath)
    {
        string cmPath = Path.Combine(ThisTestRoot, cargoRelPath);
        var cargo = Manifest.Create(cmPath);

        cargo.WorkspaceRoot.Should().Be(Path.Combine(ThisTestRoot, workspaceRelPath));
        cargo.GetTargetPathForProfile("dev").Should().Be(Path.Combine(ThisTestRoot, targetFileRelPath));
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
        var cargo = Manifest.Create(Path.Combine(ThisTestRoot, manifestPath));

        cargo.GetTargetPathForProfileRelativeToPath("dev", Path.Combine(ThisTestRoot, filePath)).Should().Be(ret);
    }

    [Theory]
    [InlineData(@"hello_library\src\lib.rs", @"hello_library\Cargo.toml", true)]
    [InlineData(@"hello_library\Cargo.toml", @"hello_library\Cargo.toml", true)]
    [InlineData(@"hello_workspace\main\src\main.rs", @"hello_workspace\main\Cargo.toml", true)]
    [InlineData(@"hello_workspace\main\Cargo.toml", @"hello_workspace\main\Cargo.toml", true)]
    [InlineData(@"not_a_project\src\main.rs", @"not_a_project\Cargo.toml", false)]
    public void GetParentCargoManifestTests(string projFilePath, string parentCargoRelPath, bool foundParentManifest)
    {
        string path = Path.Combine(ThisTestRoot, projFilePath);
        var workspaceRoot = Path.Combine(ThisTestRoot, Path.GetDirectoryName(parentCargoRelPath));
        var found = Manifest.TryGetParentManifest(workspaceRoot, path, out string parentCargoPath);

        found.Should().Be(foundParentManifest);
        var expectedParentManifestpath = found ? Path.Combine(ThisTestRoot, parentCargoRelPath) : null;
        parentCargoPath.Should().Be(expectedParentManifestpath);
    }
}
