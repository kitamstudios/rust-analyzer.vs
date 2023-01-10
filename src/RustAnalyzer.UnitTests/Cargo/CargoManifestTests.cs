using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using KS.RustAnalyzer.Cargo;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Cargo;

public class CargoManifestTests
{
    private static readonly string _thisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData");

    [Fact]
    public void SimplestExe()
    {
        string cmPath = Path.Combine(_thisTestRoot, @"hello_world\Cargo.toml");
        var cargo = CargoManifest.Create(cmPath);

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
        string cmPath = Path.Combine(_thisTestRoot, @"hello_library\Cargo.toml");
        var cargo = CargoManifest.Create(cmPath);

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
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\target\debug\hello_lib.rlib")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\target\debug\main.exe")]
    public void WorkspaceRootTests(string cargoRelPath, string workspaceRelPath, string targetFileRelPath)
    {
        string cmPath = Path.Combine(_thisTestRoot, cargoRelPath);
        var cargo = CargoManifest.Create(cmPath);

        cargo.WorkspaceRoot.Should().Be(Path.Combine(_thisTestRoot, workspaceRelPath));
        cargo.GetTargetPathForProfile("dev").Should().Be(Path.Combine(_thisTestRoot, targetFileRelPath));
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
        var cargo = CargoManifest.Create(Path.Combine(_thisTestRoot, manifestPath));

        cargo.GetTargetPathForProfileRelativeToPath("dev", Path.Combine(_thisTestRoot, filePath)).Should().Be(ret);
    }

    [Theory]
    [InlineData(@"hello_library\src\lib.rs", @"hello_library\Cargo.toml")]
    [InlineData(@"hello_library\Cargo.toml", @"hello_library\Cargo.toml")]
    [InlineData(@"hello_workspace\main\src\main.rs", @"hello_workspace\main\Cargo.toml")]
    [InlineData(@"hello_workspace\main\Cargo.toml", @"hello_workspace\main\Cargo.toml")]
    public void GetParentCargoManifestTests(string projFilePath, string parentCargoRelPath)
    {
        string path = Path.Combine(_thisTestRoot, projFilePath);
        var cargo = CargoManifest.GetParentCargoManifest(path, null, out string parentCargoPath);

        parentCargoPath.Should().Be(Path.Combine(_thisTestRoot, parentCargoRelPath));
    }
}
