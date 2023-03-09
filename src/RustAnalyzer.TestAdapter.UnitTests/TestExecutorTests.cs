using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

// TODO: test for both APIs for executor
// TODO: tests for filter
public class TestExecutorTests
{
    private readonly IToolChainService _tcs = new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L);

    [Theory(Skip = "Rust nightlies do not contain the necessary changes yet.")]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public async Task RunTestsTestsAsync(string workspaceRelRoot, string containerName)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath("dev");
        var tcPath = targetPath + (PathEx)containerName;
        targetPath.CleanTestContainers();

        await _tcs.DoBuildAsync(workspacePath, manifestPath, "dev");
        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(new[] { (string)tcPath }, Mock.Of<IRunContext>(), fh);

        var normalizedStr = fh.Results
            .OrderBy(x => x.TestCase.FullyQualifiedName).ThenBy(x => x.TestCase.LineNumber)
            .SerializeObject(Formatting.Indented)
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        normalizedStr = Regex.Replace(normalizedStr, @"    ""(Start|End)Time"": ""(.*)"",", string.Empty);
        Approvals.Verify(normalizedStr);
    }
}
