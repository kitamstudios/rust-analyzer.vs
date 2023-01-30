using System;
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

public class TargetTests
{
    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [InlineData(@"hello_library\Cargo.toml")]
    [InlineData(@"hello_workspace\Cargo.toml")]
    [InlineData(@"hello_workspace\main\Cargo.toml")]
    [InlineData(@"hello_workspace\shared\Cargo.toml")]
    [InlineData(@"hello_workspace2\Cargo.toml")]
    [InlineData(@"hello_workspace2\shared\Cargo.toml")]
    [InlineData(@"hello_workspace2\shared2\Cargo.toml")]
    [InlineData(@"hello_world\Cargo.toml")]
    [InlineData(@"workspace_mixed\Cargo.toml")]
    [InlineData(@"workspace_mixed\shared\Cargo.toml")]
    [InlineData(@"workspace_with_tests\Cargo.toml")]
    [InlineData(@"workspace_with_tests\adder\Cargo.toml")]
    [InlineData(@"workspace_with_tests\add_one\Cargo.toml")]
    public void ManifestTargetsTests(string manifestRelPath)
    {
        NamerFactory.AdditionalInformation = manifestRelPath.ReplaceInvalidChars();
        string manifestPath = Path.Combine(TestHelpers.ThisTestRoot, manifestRelPath);

        var manifest = Manifest.Create(manifestPath);
        var targets = manifest.Targets.Select(
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
