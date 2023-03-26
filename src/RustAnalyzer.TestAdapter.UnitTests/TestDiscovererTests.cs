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
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class TestDiscovererTests
{
    private readonly IToolChainService _tcs = new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L);

    [Theory]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests", "dev")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests", "dev")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public async Task DiscoverTestsTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        var tcPath = tps.TargetPath + (PathEx)containerName;
        tps.TargetPath.CleanTestContainers();

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile);
        var sink = new SpyTestCaseDiscoverySink();
        new TestDiscoverer().DiscoverTests(new[] { (string)tcPath }, Mock.Of<IDiscoveryContext>(), Mock.Of<IMessageLogger>(), sink);

        var normalizedStr = sink.TestCases
            .OrderBy(x => x.FullyQualifiedName).ThenBy(x => x.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"bin_with_example", "hello_world_hello_world.rusttests", "dev")]
    [UseReporter(typeof(DiffReporter))]
    public async Task AdditionalBuildArgsTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        var tcPath = tps.TargetPath + (PathEx)containerName;
        tps.TargetPath.CleanTestContainers();

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile, additionalBuildArgs: @"--config ""build.rustflags = '--cfg foo'""", additionalTestDiscoveryArguments: "--config\0build.rustflags = '--cfg foo'\0\0");
        var sink = new SpyTestCaseDiscoverySink();
        new TestDiscoverer().DiscoverTests(new[] { (string)tcPath }, Mock.Of<IDiscoveryContext>(), Mock.Of<IMessageLogger>(), sink);

        var normalizedStr = sink.TestCases
            .OrderBy(x => x.FullyQualifiedName).ThenBy(x => x.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }
}
