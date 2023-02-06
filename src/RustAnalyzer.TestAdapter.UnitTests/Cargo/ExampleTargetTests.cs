using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class ExampleTargetTests
{
    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [InlineData(@"hello_library\Cargo.toml", "hello_library")]
    [InlineData(@"hello_workspace\Cargo.toml", "hello_workspace")]
    [InlineData(@"lib_with_example\Cargo.toml", "lib_with_example")]
    [InlineData(@"bin_with_example\Cargo.toml", "bin_with_example")]
    [InlineData(@"workspace_with_example\lib\Cargo.toml", "workspace_with_example")]
    public void GetAllTests(string manifestRelPath, string workspaceRootRel)
    {
        NamerFactory.AdditionalInformation = manifestRelPath.ReplaceInvalidChars();
        string manifestPath = Path.Combine(TestHelpers.ThisTestRoot, manifestRelPath);
        string workspaceRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootRel);

        var targets = ExampleTarget.GetAll(Manifest.Create(manifestPath, workspaceRoot));
        var targetObjs = targets.Cast<ExampleTarget>().Select(
            t => new
            {
                t.Name,
                t.IsRunnable,
                t.TargetFileName,
                t.QualifiedTargetFileName,
                Source = t.Source.RemoveMachineSpecificPaths(),
                Type = t.Type.ToString(),
                Manifest = t.Manifest.FullPath.RemoveMachineSpecificPaths(),
                Path = t.GetPathRelativeTo("dev", TestHelpers.ThisTestRoot),
                t.AdditionalBuildArgs,
            });
        Approvals.VerifyAll(targetObjs.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [InlineData(@"hello_library\Cargo.toml", "hello_library")]
    [InlineData(@"hello_workspace\Cargo.toml", "hello_workspace")]
    [InlineData(@"lib_with_example\Cargo.toml", "lib_with_example")]
    [InlineData(@"bin_with_example\Cargo.toml", "bin_with_example")]
    [InlineData(@"workspace_with_example\lib\Cargo.toml", "workspace_with_example")]
    public async Task GetAllTestsAsync(string manifestRelPath, string workspaceRootRel)
    {
        NamerFactory.AdditionalInformation = manifestRelPath.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot2.Combine((PathEx)manifestRelPath);
        var workspaceRoot = TestHelpers.ThisTestRoot2.Combine((PathEx)workspaceRootRel);

        var package = await workspaceRoot.MS().GetPackageAsync(manifestPath, default);
        var targets = package.GetTargets().Where(t => t.Kinds[0] == Workspace.Kind.Example);

        var targetObjs = targets.Select(
            t => new
            {
                t.Name,
                t.IsRunnable,
                t.TargetFileName,
                t.QualifiedTargetFileName,
                Source = t.SourcePath.RemoveMachineSpecificPaths(),
                Type = t.Kinds[0].ToString(),
                Manifest = t.Parent.FullPath.RemoveMachineSpecificPaths(),
                Path = t.GetPathRelativeTo("dev", TestHelpers.ThisTestRoot),
                t.AdditionalBuildArgs,
            });
        Approvals.VerifyAll(targetObjs.Select(o => o.SerializeObject(Formatting.Indented, new PathExJsonConverter())), label: string.Empty);
    }
}