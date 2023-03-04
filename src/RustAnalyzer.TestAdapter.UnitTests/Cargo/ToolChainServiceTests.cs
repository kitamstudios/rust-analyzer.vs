using System;
using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public sealed class ToolChainServiceTests
{
    [Theory]
    [InlineData(@"hello_world")]
    [InlineData(@"hello_workspace")]
    [InlineData(@"workspace_mixed")]
    [UseReporter(typeof(DiffReporter))]
    public async Task GetMetadataTestsAsync(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L).GetWorkspaceAsync(manifestPath, default);

        var normalizedStr = wmd
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"hello_world")]
    [InlineData(@"hello_library")]
    [InlineData(@"hello_workspace")]
    [InlineData(@"workspace_mixed")]
    public async Task CheckParentsTestsAsync(string workspaceRelRoot)
    {
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L).GetWorkspaceAsync(manifestPath, default);
        var targetParents = wmd.Packages.Select(p => (p, tp: p.Targets.Select(t => t.Parent)));

        wmd.Packages.Should().OnlyContain(p => p.Parent == wmd);
        targetParents.Should().OnlyContain(e => e.tp.All(p => p == e.p));
    }

    [Theory]
    [InlineData(@"hello_world")]
    [InlineData(@"hello_world/src/..")]
    [InlineData(@"hello_library")]
    [InlineData(@"workspace_mixed")]
    public async Task RootPackageIsNotAddedAsync(string workspaceRelRoot)
    {
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L).GetWorkspaceAsync(manifestPath, default);

        wmd.Packages.Should().NotContain(p => p.Name == Workspace.Package.RootPackageName || !p.IsPackage);
        wmd.Packages.Should().OnlyContain(p => p.IsPackage);
    }

    [Theory]
    [InlineData(@"hello_workspace")]
    [InlineData(@"hello_workspace/main/..")]
    public async Task RootPackageIsAddedAsync(string workspaceRelRoot)
    {
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L).GetWorkspaceAsync(manifestPath, default);

        wmd.Packages.Should().ContainSingle(p => p.Name == Workspace.Package.RootPackageName && !p.IsPackage);
    }

    [Theory]
    [InlineData(@"hello_world")] // No tests.
    [InlineData(@"hello_library")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public async Task GetTestSuiteTestsAsync(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var testSuite = await new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L).GetTestSuiteAsync(manifestPath, default);

        var normalizedStr = testSuite
            .OrderBy(x => x.FQN).ThenBy(x => x.StartLine)
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        Approvals.Verify(normalizedStr);
    }
}
