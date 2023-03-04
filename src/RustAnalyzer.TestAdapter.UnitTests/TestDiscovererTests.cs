using System;
using System.Linq;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
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
    [Theory(Skip = "rustc changes not in nightlies yet.")]
    [InlineData(@"hello_world")] // No tests.
    [InlineData(@"hello_library")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public void DiscoverTestsTests(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var sink = new SpyTestCaseDiscoverySink();
        new TestDiscoverer().DiscoverTests(new[] { (string)manifestPath }, Mock.Of<IDiscoveryContext>(), Mock.Of<IMessageLogger>(), sink);

        var normalizedStr = sink.TestCases
            .OrderBy(x => x.FullyQualifiedName).ThenBy(x => x.LineNumber)
            .SerializeObject(Formatting.Indented)
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        Approvals.Verify(normalizedStr);
    }
}
