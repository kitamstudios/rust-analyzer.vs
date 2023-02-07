using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class WorkspaceExtensionsTests
{
    [Theory]
    [InlineData(@"hello_library\src\lib.rs", "hello_library", @"hello_library\Cargo.toml")]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", @"hello_library\Cargo.toml")]
    [InlineData(@"hello_workspace\main\src\main.rs", "hello_workspace", @"hello_workspace\main\Cargo.toml")]
    [InlineData(@"hello_workspace\main\src", "hello_workspace", @"hello_workspace\main\Cargo.toml")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", @"hello_workspace\main\Cargo.toml")]
    [InlineData(@"workspace_with_example\lib\examples\eg1.rs", "workspace_with_example", @"workspace_with_example\lib\Cargo.toml")]
    public void GetContainingManifestOrThisTests(string fileOrFolder, string workspaceRelRoot, string parentCargoRelPath)
    {
        var path = TestHelpers.ThisTestRoot.Combine((PathEx)fileOrFolder);
        var workspaceRoot = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot);
        var found = path.TryGetParentManifestOrThisUnderWorkspace(workspaceRoot, out PathEx? parentCargoPath);

        found.Should().BeTrue();
        parentCargoPath.Should().Be(TestHelpers.ThisTestRoot.Combine((PathEx)parentCargoRelPath));
    }

    [Theory]
    [InlineData(@"c:\workspace_with_example\lib\examples\eg1.rs", "workspace_with_example")]
    [InlineData(@"not_a_project\src\main.rs", "not_a_project")]
    [InlineData(@"not_a_project\src", "not_a_project")]
    public void GetContainingManifestOrThisForInvalidTests(string fileOrFolder, string workspaceRelRoot)
    {
        var path = TestHelpers.ThisTestRoot.Combine((PathEx)fileOrFolder);
        var workspaceRoot = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot);
        var found = path.TryGetParentManifestOrThisUnderWorkspace(workspaceRoot, out PathEx? parentCargoPath);

        parentCargoPath.Should().BeNull();
        found.Should().BeFalse();
    }

    [Theory]
    [InlineData(@"not_a_project\src\main.rs", "not_a_project", false)]
    [InlineData(@"hello_library\src\lib.rs", "hello_library", false)]
    [InlineData(@"hello_library\Cargo.toml", "hello_library", false)]
    [InlineData(@"hello_workspace\main\src\main.rs", "hello_workspace", true)]
    [InlineData(@"hello_workspace\main\src\main.txt", "hello_workspace", false)]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg1.rs", "workspace_with_example", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg2\main.rs", "workspace_with_example", true)]
    [InlineData(@"workspace_with_example\lib\examples\eg2\utils.rs", "workspace_with_example", false)]
    [InlineData(@"does_not_exist\workspace_with_example\lib\examples\eg1.rs", "does_not_exist", false)]
    public async Task CanHaveExecutableTargetsTestsAsync(string relativePath, string relWorkspaceRoot, bool canHaveExecutableTargets)
    {
        var filePath = TestHelpers.ThisTestRoot.Combine((PathEx)relativePath);
        var workspaceRoot = TestHelpers.ThisTestRoot.Combine((PathEx)relWorkspaceRoot);

        var res = await TestHelpers.MS(workspaceRoot).CanHaveExecutableTargetsAsync(filePath, default);

        res.Should().Be(canHaveExecutableTargets);
    }
}
