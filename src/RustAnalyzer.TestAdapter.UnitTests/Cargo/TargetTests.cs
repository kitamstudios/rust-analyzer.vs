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

public class TargetTests
{
    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [InlineData(@"lib_with_example\Cargo.toml", "lib_with_example")]
    [InlineData(@"bin_with_example\Cargo.toml", "bin_with_example")]
    [InlineData(@"hello_library\Cargo.toml", "hello_library")]
    [InlineData(@"hello_workspace\Cargo.toml", "hello_workspace")]
    [InlineData(@"hello_workspace\main\Cargo.toml", "hello_workspace")]
    [InlineData(@"hello_workspace\shared\Cargo.toml", "hello_workspace")]
    [InlineData(@"hello_workspace2\Cargo.toml", "hello_workspace2")]
    [InlineData(@"hello_workspace2\shared\Cargo.toml", "hello_workspace2")]
    [InlineData(@"hello_workspace2\shared2\Cargo.toml", "hello_workspace2")]
    [InlineData(@"hello_world\Cargo.toml", "hello_world")]
    [InlineData(@"workspace_mixed\Cargo.toml", "workspace_mixed")]
    [InlineData(@"workspace_mixed\shared\Cargo.toml", "workspace_mixed")]
    [InlineData(@"workspace_with_tests\Cargo.toml", "workspace_with_tests")]
    [InlineData(@"workspace_with_tests\adder\Cargo.toml", "workspace_with_tests")]
    [InlineData(@"workspace_with_tests\add_one\Cargo.toml", "workspace_with_tests")]
    [InlineData(@"workspace_with_example\lib\Cargo.toml", "workspace_with_example")]
    public async Task ManifestTargetsTestsAsync(string manifestRelPath, string workspaceRootRel)
    {
        NamerFactory.AdditionalInformation = manifestRelPath.ReplaceInvalidChars();
        string wkRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootRel);
        string manifestPath = Path.Combine(TestHelpers.ThisTestRoot, manifestRelPath);

        var manifest = Manifest.Create(manifestPath, wkRoot);
        var targets = (await manifest.GetTargets()).Select(
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
        Approvals.VerifyAll(targets.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }
}
