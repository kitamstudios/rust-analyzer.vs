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
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class TestExecutorTests : TestsWithLogger
{
    private readonly IToolchainService _tcs = new ToolchainService(TestHelpers.TL.T, TestHelpers.TL.L);

    public TestExecutorTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Theory]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests", "bench")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests", "bench")] // Has tests.
    [UseReporter(typeof(RaVsDiffReporter))]
    public async Task RunTestsTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        var tcPath = tps.TargetPath + (PathEx)containerName;
        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile, additionalTestExecutionArguments: "--exclude-should-panic", testExecutionEnvironment: "ENV_VAR_1=ENV_VAR_1_VALUE\0\0");
        new TestDiscoverer().DiscoverTests(tcPath, Mock.Of<IDiscoveryContext>(), MessageLogger, Mock.Of<ITestCaseDiscoverySink>());

        new TestExecutor().RunTests(tcPath, Mock.Of<IRunContext>(), FrameworkHandle);

        var normalizedStr = FrameworkHandle.Results
            .OrderBy(x => x.TestCase.FullyQualifiedName).ThenBy(x => x.TestCase.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"workspace_with_tests", new[] { "add_one_libadd_one|add_one.tests.fibonacci_test.case_2", "adder_adder|adder.tests.it_works_failing", "adder_adder|adder.tests1.tests1.it_works_skipped2", "adder_adder|integration_tests.integration_test_1" }, "test")]
    public async Task RunSelectedTestsFromMultiplePackagesMultipleFilesTestsAsync(string workspaceRelRoot, string[] tests, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);

        var testCases = tests.Select(t => t.Split('|')).Select(x => new TestCase { Source = $"{tps.TargetPath + x[0]}{Constants.TestsContainerExtension}", FullyQualifiedName = x[1], });

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile);
        new TestDiscoverer().DiscoverTests(testCases.Select(tc => tc.Source), Mock.Of<IDiscoveryContext>(), MessageLogger, Mock.Of<ITestCaseDiscoverySink>());

        new TestExecutor().RunTests(testCases, Mock.Of<IRunContext>(), FrameworkHandle);

        FrameworkHandle.Results.Select(r => $"{((PathEx)r.TestCase.Source).GetFileNameWithoutExtension()}|{r.DisplayName}").Should().BeEquivalentTo(tests);
    }
}
