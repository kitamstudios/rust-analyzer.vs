using FluentAssertions;
using KS.RustAnalyzer.Cargo;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Cargo;

public class CargoManifestTests
{
    [Theory]
    [InlineData(
        @"D:\src\ks\rust-analyzer\src\TestProjects\hello_world\Cargo.toml",
        @"D:\src\ks\rust-analyzer\src\TestProjects\hello_world\src\main.rs",
        @"..\target\debug\hello_world.exe")]
    [InlineData(
        @"D:\src\ks\rust-analyzer\src\TestProjects\hello_world\Cargo.toml",
        @"D:\src\ks\rust-analyzer\src\TestProjects\hello_world\Cargo.toml",
        @"target\debug\hello_world.exe")]
    public void GetTargetPathForProfileRelativeToPathTests(string manifestPath, string filePath, string ret)
    {
        var cargo = CargoManifest.Create(manifestPath);

        cargo.GetTargetPathForProfileRelativeToPath("dev", filePath).Should().Be(ret);
    }
}
