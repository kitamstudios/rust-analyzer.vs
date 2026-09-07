using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

[Trait("type", "IntegrationTests")]
public class TestDiscovererTests : TestsWithLogger
{
    private readonly IToolchainService _tcs =
        new ToolchainService(TestHelpers.Telemetry, TestHelpers.LoggerFactory);

    public TestDiscovererTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Theory]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests", "dev")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests", "dev")] // Has tests.
    [UseReporter(typeof(RaVsDiffReporter))]
    public async Task DiscoverTestsTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        var tcPath = tps.TargetPath + (PathEx)containerName;

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile);
        var sink = new SpyTestCaseDiscoverySink();
        var messages = new List<string>();
        var logger = new Mock<IMessageLogger>();
        logger
            .Setup(
                value => value.SendMessage(
                    It.IsAny<TestMessageLevel>(),
                    It.IsAny<string>()))
            .Callback<TestMessageLevel, string>(
                (_, message) => messages.Add(message));
        new TestDiscoverer().DiscoverTests(
            tcPath,
            Mock.Of<IDiscoveryContext>(),
            logger.Object,
            sink);

        var normalizedStr = sink.TestCases
            .OrderBy(x => x.FullyQualifiedName).ThenBy(x => x.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
        messages.Should().Contain(
            message => message.Contains(
                typeof(TestDiscovererCommon).FullName));
        messages.Should().Contain(
            message => message.Contains(typeof(ToolchainService).FullName));
        messages.Should().Contain(
            message => message.Contains(typeof(ProcessRunner).FullName));
        messages.Should().NotContain(
            message => message.Contains(
                "KS.RustAnalyzer.TestAdapter.Legacy"));
    }

    [Theory]
    [InlineData(@"bin_with_example", "hello_world_hello_world.rusttests", "dev")]
    [UseReporter(typeof(RaVsDiffReporter))]
    public async Task AdditionalBuildArgsTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        var tcPath = tps.TargetPath + (PathEx)containerName;

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile, additionalBuildArgs: @"--config ""build.rustflags = '--cfg foo'""", additionalTestDiscoveryArguments: "--config\0build.rustflags = '--cfg foo'\0\0");
        var sink = new SpyTestCaseDiscoverySink();
        new TestDiscoverer().DiscoverTests(tcPath, Mock.Of<IDiscoveryContext>(), MessageLogger, sink);

        var normalizedStr = sink.TestCases
            .OrderBy(x => x.FullyQualifiedName).ThenBy(x => x.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }
}
