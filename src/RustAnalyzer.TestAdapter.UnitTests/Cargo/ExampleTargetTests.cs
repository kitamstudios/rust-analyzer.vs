using System.IO;
using System.Linq;
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
    [InlineData(@"hello_library\Cargo.toml")]
    [InlineData(@"hello_workspace\Cargo.toml")]
    [InlineData(@"lib_with_example\Cargo.toml")]
    [InlineData(@"workspace_with_example\lib\Cargo.toml")]
    public void GetAllTests(string manifestRelPath)
    {
        NamerFactory.AdditionalInformation = manifestRelPath.ReplaceInvalidChars();
        string manifestPath = Path.Combine(TestHelpers.ThisTestRoot, manifestRelPath);

        var targets = ExampleTarget.GetAll(Manifest.Create(manifestPath));
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
}