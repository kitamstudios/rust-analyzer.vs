using System.IO;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class ManifestExtensionsTests
{
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
    public void GetContainingManifestOrThisTests(string fileOrFolder, string workspaceRelRoot, string parentCargoRelPath, bool foundParentManifest)
    {
        string path = Path.Combine(TestHelpers.ThisTestRoot, fileOrFolder);
        var workspaceRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRelRoot);
        var found = path.TryGetParentManifestOrThisUnderWorkspace(workspaceRoot, out string parentCargoPath);

        found.Should().Be(foundParentManifest);
        var expectedParentManifestpath = found ? Path.Combine(TestHelpers.ThisTestRoot, parentCargoRelPath) : null;
        parentCargoPath.Should().Be(expectedParentManifestpath);
    }

    [Theory]
    [InlineData(@"not_a_project\src\main.rs", "not_a_project", false)]
    [InlineData(@"hello_library\src\lib.rs", "hello_library", false)]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", false)]
    [InlineData(@"hello_workspace\main\src\main.rs", "hello_workspace", false)]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg1.rs", "workspace_with_example", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg2\main.rs", "workspace_with_example", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg2\utils.rs", "workspace_with_example", false)]
    [InlineData(@"does_not_exist\workspace_with_example\lib\examples\eg1.rs", "does_not_exist", false)]
    public void CanHaveExecutableTargetsTests(string relativePath, string relWorkspaceRoot, bool canHaveExecutableTargets)
    {
        var filePath = Path.Combine(TestHelpers.ThisTestRoot, relativePath);
        var workspaceRoot = Path.Combine(TestHelpers.ThisTestRoot, relWorkspaceRoot);

        var res = filePath.CanHaveExecutableTargets(workspaceRoot);

        res.Should().Be(canHaveExecutableTargets);
    }
}
