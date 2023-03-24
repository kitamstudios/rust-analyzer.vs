using System.IO;
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
    private readonly IToolChainService _tcs = new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L);

    [Theory]
    [InlineData(@"hello_world")]
    [InlineData(@"hello_workspace")]
    [InlineData(@"workspace_mixed")]
    [UseReporter(typeof(DiffReporter))]
    public async Task GetMetadataTestsAsync(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await _tcs.GetWorkspaceAsync(manifestPath, default);

        var normalizedStr = wmd.SerializeAndNormalizeObject();
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

        var wmd = await _tcs.GetWorkspaceAsync(manifestPath, default);
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

        var wmd = await _tcs.GetWorkspaceAsync(manifestPath, default);

        wmd.Packages.Should().NotContain(p => p.Name == Workspace.Package.RootPackageName || !p.IsPackage);
        wmd.Packages.Should().OnlyContain(p => p.IsPackage);
    }

    [Theory]
    [InlineData(@"hello_workspace")]
    [InlineData(@"hello_workspace/main/..")]
    public async Task RootPackageIsAddedAsync(string workspaceRelRoot)
    {
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await _tcs.GetWorkspaceAsync(manifestPath, default);

        wmd.Packages.Should().ContainSingle(p => p.Name == Workspace.Package.RootPackageName && !p.IsPackage);
    }

    // TODO: 3. RELEASE: during build, fmt, clippy etc. save all open files.
    [Theory]
    [InlineData(@"hello_world", "dev")]
    [InlineData(@"hello_library", "dev")]
    [InlineData(@"hello_workspace", "dev")]
    [InlineData(@"workspace_mixed", "dev")]
    [UseReporter(typeof(DiffReporter))]
    public async Task BuildTestsAsync(string workspaceRelRoot, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath(profile);
        targetPath.CleanTestContainers();

        var success = await _tcs.DoBuildAsync(workspacePath, manifestPath, profile);

        success.Should().BeTrue();
        var tasks = Directory.EnumerateFiles(targetPath, TestHelpers.TestContainersSearchPattern)
            .Select(async f => (path: (PathEx)f, container: JsonConvert.DeserializeObject<TestContainer>(await ((PathEx)f).ReadAllTextAsync(default))));
        var tcs = await Task.WhenAll(tasks);
        var normalizedStr = tcs.SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"bin_with_example", "dev")]
    public async Task AdditionalBuildArgsTestsAsync(string workspaceRelRoot, string profile)
    {
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;

        var success = await _tcs.DoBuildAsync(workspacePath, manifestPath, profile, additionalBuildArgs: @"--config ""build.rustflags = '--cfg foo=\""bar\""'""");

        success.Should().BeTrue();
    }

    [Theory]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests", "release")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests", "release")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public async Task GetTestSuiteTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath(profile);
        var tcPath = targetPath + (PathEx)containerName;
        targetPath.CleanTestContainers();

        await _tcs.DoBuildAsync(workspacePath, manifestPath, profile);
        var testSuite = await _tcs.GetTestSuiteInfoAsync(tcPath, profile, default);
        var tc = JsonConvert.DeserializeObject<TestContainer>(await tcPath.ReadAllTextAsync(default));

        tc.TestExe.FileExists().Should().BeTrue();
        tc.TestExe.GetExtension().Should().Be((PathEx)".exe");
        tc.Profile.Should().Be(profile);
        tc.ThisPath.Should().Be(tcPath);
        testSuite.Container.Should().Be(tcPath);
        var normalizedStr = testSuite.SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }
}
