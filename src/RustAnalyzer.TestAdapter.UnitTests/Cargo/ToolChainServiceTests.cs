using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    [UseReporter(typeof(RaVsDiffReporter))]
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

    // TODO: NEW: during build, fmt, clippy etc. save all open files.
    [Theory]
    [InlineData(@"hello_world", "dev")]
    [InlineData(@"hello_library", "dev")]
    [InlineData(@"hello_workspace", "dev")]
    [InlineData(@"workspace_mixed", "dev")]
    [UseReporter(typeof(RaVsDiffReporter))]
    public async Task BuildTestsAsync(string workspaceRelRoot, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath(profile);

        var success = await _tcs.DoBuildAsync(workspacePath, manifestPath, profile);

        success.Should().BeTrue();
        var tasks = Directory.EnumerateFiles(targetPath, Constants.TestContainersSearchPattern)
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

        var success = await _tcs.DoBuildAsync(workspacePath, manifestPath, profile, additionalBuildArgs: @"--config ""build.rustflags = '--cfg foo'""");

        success.Should().BeTrue();
    }

    [Theory]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests", "release", new string[] { "hello_world-*.exe" })] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests", "release", new[] { "hello_lib-*.exe", "int_tests-*.exe" })] // Has tests.
    [UseReporter(typeof(RaVsDiffReporter))]
    public async Task GetTestSuiteTestsAsync(string workspaceRelRoot, string containerName, string profile, string[] testExes)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath(profile);
        var tcPath = targetPath + (PathEx)containerName;

        await _tcs.DoBuildAsync(workspacePath, manifestPath, profile);
        var testSuites = await (await _tcs.GetTestSuiteInfoAsync(tcPath, profile, default)).ToTaskEnumerableAsync();
        var tc = JsonConvert.DeserializeObject<TestContainer>(await tcPath.ReadAllTextAsync(default));

        tc.TestExes.All(e => e.FileExists()).Should().BeTrue();
        tc.TestExes.Select(e => Regex.Replace(e.GetFileName(), @"\-[\da-f]{16}\.", "-*.", RegexOptions.IgnoreCase)).Should().BeEquivalentTo(testExes);
        tc.Profile.Should().Be(profile);
        tc.ThisPath.Should().Be(tcPath);
        testSuites.Should().HaveCount(testExes.Length);
        testSuites.Select(x => x.Container.ThisPath).Should().OnlyContain(x => x == tcPath);
        var normalizedStr = testSuites.OrderBy(x => (string)x.Exe).SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }
}
