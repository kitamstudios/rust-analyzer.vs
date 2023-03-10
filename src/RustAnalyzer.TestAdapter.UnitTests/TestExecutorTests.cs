using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

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
        var tps = workspaceRelRoot.GetTestPaths("bench");
        var tcPath = tps.TargetPath + (PathEx)containerName;
        tps.TargetPath.CleanTestContainers();

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, "bench");
        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(new[] { (string)tcPath }, Mock.Of<IRunContext>(), fh);

        var normalizedStr = fh.Results
            .OrderBy(x => x.TestCase.FullyQualifiedName).ThenBy(x => x.TestCase.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"workspace_with_tests", new[] { "add_one_libadd_one|tests.fibonacci_test.case_2", "adder_adder|tests.it_works_failing", "adder_adder|tests1.tests1.it_works_skipped2" })]
    public async Task RunSelectedTestsFromMultiplePackagesMultipleFilesTestsAsync(string workspaceRelRoot, string[] tests)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths("test");
        tps.TargetPath.CleanTestContainers();

        var testCases = tests.Select(t => t.Split('|')).Select(x => new TestCase { Source = $"{tps.TargetPath + x[0]}{Constants.TestsContainerExtension}", FullyQualifiedName = x[1], });

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, "test");
        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(testCases, Mock.Of<IRunContext>(), fh);

        fh.Results.Select(r => $"{((PathEx)r.TestCase.Source).GetFileNameWithoutExtension()}|{r.DisplayName}").Should().BeEquivalentTo(tests);
    }
}
