using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

[Trait("type", "IntegrationTests")]
public sealed class ToolchainServiceTests
{
    private readonly IToolchainService _tcs = new ToolchainService(TestHelpers.TL.T, TestHelpers.TL.L);

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
        var normalizedStr = testSuites.OrderBy(x => (string)x.Exe.GetFileName(), StringComparer.OrdinalIgnoreCase).SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }

    [Fact]
    public async Task MetadataFailureUsesToolchainAndProcessCategoriesAsync()
    {
        var telemetry = new RecordingFeatureUsageTelemetry();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var service = new ToolchainService(telemetry, factory);
        var manifestPath = TestHelpers.ThisTestRoot.Combine(
            (PathEx)"missing-workspace",
            Constants.ManifestFileName2);

        Func<Task> getWorkspace = async () =>
            await service.GetWorkspaceAsync(manifestPath, default);

        var exception = (await getWorkspace.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        provider.Entries.Should().Contain(
            entry => entry.Category == typeof(ProcessRunner).FullName
                && entry.EventId == new EventId(1, "ProcessStarted")
                && entry.Level == LogLevel.Information);
        provider.Entries.Should().Contain(
            entry => entry.Category == typeof(ProcessRunner).FullName
                && entry.EventId == new EventId(2, "ProcessFinished")
                && entry.Level == LogLevel.Information);
        var failure = provider.Entries.Single(
            entry => entry.EventId.Id == 2
                && entry.Category == typeof(ToolchainService).FullName);
        failure.EventId.Name.Should().Be("WorkspaceMetadataFailed");
        failure.Level.Should().Be(LogLevel.Error);
        failure.Template.Should().Be(
            "Unable to obtain metadata for file {ManifestPath}.");
        failure.Properties["ManifestPath"].Should().Be(manifestPath);
        failure.Exception.Should().BeSameAs(exception);
        telemetry.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingTestArtifactsUseStructuredToolchainContractsAsync()
    {
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var service = new ToolchainService(TestHelpers.TL.T, factory);
        var paths = "hello_world".GetTestPaths("release");
        var testContainerPath = paths.TargetPath
            + (PathEx)"hello_world_hello_world.rusttests";
        await service.DoBuildAsync(
            paths.WorkspacePath,
            paths.ManifestPath,
            "release",
            additionalTestDiscoveryArguments: "--examples");

        Func<Task> discover = async () =>
            await service.GetTestSuiteInfoAsync(
                testContainerPath,
                "release",
                default);

        var exception = (await discover.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        var discovery = GetToolchainEntry(
            provider,
            new EventId(3, "TestSuiteDiscoveryStarted"),
            LogLevel.Information,
            "GetTestSuiteInfoAsync: Finding tests for {TestContainerPath}");
        discovery.Properties["TestContainerPath"]
            .Should().Be(testContainerPath);
        discovery.Exception.Should().BeNull();

        var cargoVersion = GetToolchainEntry(
            provider,
            new EventId(4, "CargoVersionResolved"),
            LogLevel.Information,
            "Using: {CargoVersion}");
        cargoVersion.Properties["CargoVersion"].Should().BeOfType<string>()
            .Which.Should().NotBeNullOrWhiteSpace();
        cargoVersion.Exception.Should().BeNull();

        var rustCompilerVersion = GetToolchainEntry(
            provider,
            new EventId(5, "RustCompilerVersionResolved"),
            LogLevel.Information,
            "Using: {RustCompilerVersion}");
        rustCompilerVersion.Properties["RustCompilerVersion"]
            .Should().BeOfType<string>()
            .Which.Should().NotBeNullOrWhiteSpace();
        rustCompilerVersion.Exception.Should().BeNull();

        var missingArtifacts = GetToolchainEntry(
            provider,
            new EventId(6, "TestArtifactsMissing"),
            LogLevel.Error,
            "Cargo produced no structured test executable artifacts. Command line '{Arguments}'. Exit code: {ExitCode}");
        missingArtifacts.Properties["Arguments"].Should().BeOfType<string>()
            .Which.Should().Contain("--examples");
        missingArtifacts.Properties["ExitCode"].Should().Be(0);
        missingArtifacts.Exception.Should().BeSameAs(exception);

        var metadataFailure = GetToolchainEntry(
            provider,
            new EventId(8, "TestSuiteMetadataFailed"),
            LogLevel.Error,
            "Unable to obtain metadata for file {ManifestPath}.");
        metadataFailure.Properties["ManifestPath"]
            .Should().Be(paths.ManifestPath);
        metadataFailure.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task TestExecutablesClearedDuringPersistenceUseStructuredErrorAsync()
    {
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var service = new ToolchainService(TestHelpers.TL.T, factory);
        var paths = "hello_world".GetTestPaths("release");
        var testContainerPath = paths.TargetPath
            + (PathEx)"hello_world_hello_world.rusttests";
        await service.DoBuildAsync(
            paths.WorkspacePath,
            paths.ManifestPath,
            "release");
        var converter = new TestExecutablesClearingConverter();
        var defaultSettings = JsonConvert.DefaultSettings;
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings
        {
            Converters = { converter, },
        };
        converter.Enabled.Value = true;

        try
        {
            var testSuites = await service.GetTestSuiteInfoAsync(
                testContainerPath,
                "release",
                default);

            testSuites.Should().BeEmpty();
        }
        finally
        {
            converter.Enabled.Value = false;
            JsonConvert.DefaultSettings = defaultSettings;
        }

        var missingExecutables = GetToolchainEntry(
            provider,
            new EventId(7, "TestExecutablesMissing"),
            LogLevel.Error,
            "GetTestSuiteInfoAsync: Something is not right. No test executables found in '{TestContainerPath}'.");
        missingExecutables.Properties["TestContainerPath"]
            .Should().Be(testContainerPath);
        missingExecutables.Exception.Should().BeNull();
    }

    [Fact]
    public async Task NonJsonTestListingUsesStructuredNightlyRequirementAsync()
    {
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var service = new ToolchainService(TestHelpers.TL.T, factory);
        var paths = "hello_world".GetTestPaths("release");
        var testContainerPath = paths.TargetPath
            + (PathEx)"hello_world_hello_world.rusttests";
        await service.DoBuildAsync(
            paths.WorkspacePath,
            paths.ManifestPath,
            "release");
        var testSuites = await service.GetTestSuiteInfoAsync(
            testContainerPath,
            "release",
            default);
        var testContainer = await testContainerPath.ReadTestContainerAsync(
            default);
        var testExecutable = testContainer.TestExes.Single();
        var backupPath = (string)testExecutable + ".t10-backup";
        File.Move(testExecutable, backupPath);

        try
        {
            File.Copy(
                Environment.GetEnvironmentVariable("ComSpec"),
                testExecutable);
            (await testSuites.Single()).Tests.Should().BeEmpty();
        }
        finally
        {
            File.Delete(testExecutable);
            File.Move(backupPath, testExecutable);
        }

        var nightlyRequired = GetToolchainEntry(
            provider,
            new EventId(9, "NightlyToolchainRequired"),
            LogLevel.Error,
            "{ExtensionName} requires nightly toolchain. Please install the nightly toolchain following instructions in https://rust-lang.github.io/rustup/concepts/channels.html. Details: Fix for https://github.com/rust-lang/rust/issues/49359 is required to support unit testing experience. The RFC process is currently underway. Till then the fix is available only in nightly toolchain.");
        nightlyRequired.Properties["ExtensionName"]
            .Should().Be("rust-analyzer.vs");
        nightlyRequired.Exception.Should().BeNull();
    }

    private static RecordingLogEntry GetToolchainEntry(
        RecordingLoggerProvider provider,
        EventId eventId,
        LogLevel level,
        string template)
    {
        var entry = provider.Entries.Single(
            candidate => candidate.Category == typeof(ToolchainService).FullName
                && candidate.EventId == eventId);
        entry.Level.Should().Be(level);
        entry.Template.Should().Be(template);
        return entry;
    }

    private sealed class TestExecutablesClearingConverter : JsonConverter
    {
        public AsyncLocal<bool> Enabled { get; } = new();

        public TestContainer Container { get; } = new();

        public override bool CanConvert(Type objectType)
        {
            return Enabled.Value && objectType == typeof(TestContainer);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            serializer.Populate(reader, Container);
            return Container;
        }

        public override void WriteJson(
            JsonWriter writer,
            object value,
            JsonSerializer serializer)
        {
            Container.TestExes = Array.Empty<PathEx>();
            Enabled.Value = false;
            try
            {
                serializer.Serialize(writer, value);
            }
            finally
            {
                Enabled.Value = true;
            }
        }
    }
}
