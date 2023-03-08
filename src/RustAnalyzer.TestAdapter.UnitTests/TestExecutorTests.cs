using System.Linq;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

// TODO: test for both APIs for discoverer
// TODO: test for both APIs for executor
public class TestExecutorTests
{
    [Theory(Skip = "Rust nightlies do not contain the necessary changes yet.")]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public void RunTestsTests(string workspaceRelRoot, string containerName)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath("dev");
        var tcPath = targetPath + (PathEx)containerName;

        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(new[] { (string)tcPath }, Mock.Of<IRunContext>(), fh);

        var normalizedStr = fh.Results.OrderBy(x => x);
        Approvals.VerifyAll(normalizedStr, string.Empty);
    }
}
