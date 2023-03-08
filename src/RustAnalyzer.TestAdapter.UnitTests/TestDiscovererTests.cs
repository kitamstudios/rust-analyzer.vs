using System;
using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class TestDiscovererTests
{
    private readonly IToolChainService _tcs = new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L);

    [Theory(Skip = "Rust nightlies do not contain the necessary changes yet.")]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public async Task DiscoverTestsTestsAsync(string workspaceRelRoot, string containerName)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath("dev");
        var tcPath = targetPath + (PathEx)containerName;

        await _tcs.DoBuildAsync(workspacePath, manifestPath, "dev");
        var sink = new SpyTestCaseDiscoverySink();
        new TestDiscoverer().DiscoverTests(new[] { (string)tcPath }, Mock.Of<IDiscoveryContext>(), Mock.Of<IMessageLogger>(), sink);

        var normalizedStr = sink.TestCases
            .OrderBy(x => x.FullyQualifiedName).ThenBy(x => x.LineNumber)
            .SerializeObject(Formatting.Indented)
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        Approvals.Verify(normalizedStr);
    }
}
