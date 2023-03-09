using System;
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
    [InlineData(@"hello_world")]
    [InlineData(@"hello_library")]
    [InlineData(@"hello_workspace")]
    [InlineData(@"workspace_mixed")]
    [UseReporter(typeof(DiffReporter))]
    public async Task BuildTestsAsync(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath("dev");
        targetPath.CleanTestContainers();

        var success = await _tcs.DoBuildAsync(workspacePath, manifestPath, "dev");

        success.Should().BeTrue();
        var tasks = Directory.EnumerateFiles(targetPath, TestHelpers.TestContainersSearchPattern)
            .Select(async f => (path: (PathEx)f, container: JsonConvert.DeserializeObject<TestContainer>(await ((PathEx)f).ReadAllTextAsync(default))));
        var tcs = await Task.WhenAll(tasks);
        var normalizedStr = tcs
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        Approvals.Verify(normalizedStr);
    }

    // TODO: tests in multiple files in the same package.
    // TODO: tests in multiple packages in the same workspace.
    [Theory(Skip = "Rust nightlies do not contain the necessary changes yet.")]
    [InlineData(@"hello_world", "hello_world_hello_world.rusttests")] // No tests.
    [InlineData(@"hello_library", "hello_lib_libhello_lib.rusttests")] // Has tests.
    [UseReporter(typeof(DiffReporter))]
    public async Task GetTestSuiteTestsAsync(string workspaceRelRoot, string containerName)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var workspacePath = TestHelpers.ThisTestRoot + (PathEx)workspaceRelRoot;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath("dev");
        var tcPath = targetPath + (PathEx)containerName;
        targetPath.CleanTestContainers();

        // TODO: passing tcPath with profile qualified path as well as profile does not seem valid.
        await _tcs.DoBuildAsync(workspacePath, manifestPath, "dev");
        var testSuite = await _tcs.GetTestSuiteInfoAsync(tcPath, "dev", default);
        var tc = JsonConvert.DeserializeObject<TestContainer>(await tcPath.ReadAllTextAsync(default));

        tc.TestExe.FileExists().Should().BeTrue();
        tc.TestExe.GetExtension().Should().Be((PathEx)".exe");
        testSuite.Container.Should().Be(tcPath);
        var normalizedStr = testSuite
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase);
        Approvals.Verify(normalizedStr);
    }
}
