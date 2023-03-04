using System.Linq;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class TestExecutorTests
{
    [Theory(Skip = "rustc changes not in nightlies yet.")]
    [InlineData(@"hello_world")] // No tests.
    [InlineData(@"hello_library")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public void RunTestsTests(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(new[] { (string)manifestPath }, Mock.Of<IRunContext>(), fh);

        var normalizedStr = fh.Results.OrderBy(x => x);
        Approvals.VerifyAll(normalizedStr, string.Empty);
    }
}
