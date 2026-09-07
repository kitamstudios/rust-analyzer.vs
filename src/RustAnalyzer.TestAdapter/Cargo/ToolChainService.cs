using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[Export(typeof(IToolchainService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ToolchainService : IToolchainService
{
    private readonly IFeatureUsageTelemetry _telemetry;
    private readonly MelLogger _logger;
    private readonly MelLogger _processLogger;
    private readonly MelLogger _buildJsonOutputParserLogger;

    [ImportingConstructor]
    public ToolchainService(
        [Import] IFeatureUsageTelemetry telemetry,
        [Import] ILoggerFactory loggerFactory)
    {
        _telemetry = EnsureArg.IsNotNull(telemetry, nameof(telemetry));
        loggerFactory = EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger(typeof(ToolchainService).FullName);
        _processLogger = loggerFactory.CreateLogger(typeof(ProcessRunner).FullName);
        _buildJsonOutputParserLogger =
            loggerFactory.CreateLogger(typeof(BuildJsonOutputParser).FullName);
    }

    public ToolchainService(IFeatureUsageTelemetry telemetry, LegacyLogger logger)
        : this(
            telemetry,
            LegacyLoggerBridge.ToMelLogger(
                EnsureArg.IsNotNull(logger, nameof(logger))))
    {
    }

    internal ToolchainService(
        IFeatureUsageTelemetry telemetry,
        MelLogger logger,
        MelLogger processLogger)
        : this(telemetry, logger, processLogger, logger)
    {
    }

    private ToolchainService(IFeatureUsageTelemetry telemetry, MelLogger logger)
        : this(telemetry, logger, logger)
    {
    }

    private ToolchainService(
        IFeatureUsageTelemetry telemetry,
        MelLogger logger,
        MelLogger processLogger,
        MelLogger buildJsonOutputParserLogger)
    {
        _telemetry = EnsureArg.IsNotNull(telemetry, nameof(telemetry));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        _processLogger =
            EnsureArg.IsNotNull(processLogger, nameof(processLogger));
        _buildJsonOutputParserLogger = EnsureArg.IsNotNull(
            buildJsonOutputParserLogger,
            nameof(buildJsonOutputParserLogger));
    }

    /// <summary>
    /// Not finding cargo.exe is a catastrophic error. Hence in prereq checks.
    /// </summary>
    public PathEx GetCargoExePath()
    {
        var cargoExePath = (PathEx)Constants.CargoExe.FindInPath();

        _logger.LogInformation(
            new EventId(1, "CargoExecutableResolved"),
            "... using {ExecutableName} from '{ExecutablePath}'.",
            Constants.CargoExe,
            cargoExePath);
        return cargoExePath;
    }

    public async Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        var success = await ExecuteOperationAsync(
            UsageOperation.CargoBuild,
            "build",
            bti.ManifestPath,
            arguments: $"build --manifest-path \"{bti.ManifestPath}\" --profile {bti.Profile} --message-format json {bti.AdditionalBuildArgs}",
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: x => BuildJsonOutputParser.Parse(
                bti.WorkspaceRoot,
                x,
                _buildJsonOutputParserLogger),
            ct: ct);

        if (success)
        {
            var w = await GetWorkspaceAsync(bti.ManifestPath, ct);
            var testContainers = w.Packages.SelectMany(p => p.GetTestContainers(bti.Profile));
            w.TargetDirectory.MakeProfilePath(bti.Profile).CleanTestContainers(testContainers.Select(x => x.Container));
            var tasks = testContainers
                .Select(x => x.Container.WriteTestContainerAsync(x.Target.Parent.ManifestPath, w.TargetDirectory, bti.AdditionalTestDiscoveryArguments, bti.AdditionalTestExecutionArguments, bti.TestExecutionEnvironment, bti.Profile, Array.Empty<PathEx>(), ct));
            await Task.WhenAll(tasks);
        }

        return success;
    }

    public Task<bool> CleanAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            UsageOperation.CargoClean,
            "clean",
            bti.ManifestPath,
            arguments: $"clean --manifest-path \"{bti.ManifestPath}\" --profile {bti.Profile}",
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: OutputPreprocessorForCargoToolsWithoutJsonOutput,
            ct: ct);
    }

    public Task<bool> RunClippyAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            UsageOperation.CargoClippy,
            "Clippy",
            bti.ManifestPath,
            arguments: $"clippy --manifest-path \"{bti.ManifestPath}\" --profile {bti.Profile} {bti.AdditionalBuildArgs}",
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: OutputPreprocessorForCargoToolsWithoutJsonOutput,
            ct: ct);
    }

    public Task<bool> RunFmtAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            UsageOperation.CargoFormat,
            "Fmt",
            bti.ManifestPath,
            arguments: $"fmt --manifest-path \"{bti.ManifestPath}\" {bti.AdditionalBuildArgs}",
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: OutputPreprocessorForCargoToolsWithoutJsonOutput,
            ct: ct);
    }

    public async Task<Workspace> GetWorkspaceAsync(PathEx manifestPath, CancellationToken ct)
    {
        var cargoFullPath = GetCargoExePath();

        try
        {
            using var proc = await ProcessRunner.RunWithLogging(
                cargoFullPath,
                new[] { "metadata", "--no-deps", "--format-version", "1", "--manifest-path", manifestPath, "--offline" },
                cargoFullPath.GetDirectoryName(),
                ImmutableDictionary<string, string>.Empty,
                ct,
                _processLogger);
            var w = JsonConvert.DeserializeObject<Workspace>(string.Join(string.Empty, proc.StandardOutputLines));
            return AddRootPackageIfNecessary(w, manifestPath);
        }
        catch (Exception e)
        {
            _logger.LogError(
                new EventId(2, "WorkspaceMetadataFailed"),
                e,
                "Unable to obtain metadata for file {ManifestPath}.",
                manifestPath);
            throw;
        }
    }

    public async Task<IEnumerable<Task<TestSuiteInfo>>> GetTestSuiteInfoAsync(PathEx testContainerPath, string profile, CancellationToken ct)
    {
        var cargoFullPath = GetCargoExePath();
        var tc = await testContainerPath.ReadTestContainerAsync(ct);
        _logger.LogInformation(
            new EventId(3, "TestSuiteDiscoveryStarted"),
            "GetTestSuiteInfoAsync: Finding tests for {TestContainerPath}",
            testContainerPath);

        try
        {
            var workingDir = tc.Manifest.GetDirectoryName();
            var cargoVersion = await ToolchainServiceExtensions.GetCommandOutputSingleLine("cargo", "--version", workingDir, ct);
            _logger.LogInformation(
                new EventId(4, "CargoVersionResolved"),
                "Using: {CargoVersion}",
                cargoVersion);
            var rustcVersion = await ToolchainServiceExtensions.GetCommandOutputSingleLine("test", "--version", workingDir, ct);
            _logger.LogInformation(
                new EventId(5, "RustCompilerVersionResolved"),
                "Using: {RustCompilerVersion}",
                rustcVersion);

            var args = new[] { "test", "--no-run", "--message-format=json", "--manifest-path", tc.Manifest, "--profile", profile }
                .Concat(tc.AdditionalTestDiscoveryArguments.FromNullSeparatedArray())
                .ToArray();

            using var proc = await ProcessRunner.RunWithLogging(
                cargoFullPath,
                args,
                workingDir,
                ImmutableDictionary<string, string>.Empty,
                ct,
                _processLogger);

            var testExes = BuildJsonOutputParser.ParseTestExecutables(proc.StandardOutputLines)
                .Distinct()
                .OrderBy(exe => (string)exe.GetFileName(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(exe => (string)exe, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (testExes.Length == 0)
            {
                var e = new InvalidOperationException(string.Format("Cargo produced no structured test executable artifacts. Command line '{0}'. Exit code: {1}", proc.Arguments, proc.ExitCode));
                _logger.LogError(
                    new EventId(6, "TestArtifactsMissing"),
                    e,
                    "Cargo produced no structured test executable artifacts. Command line '{Arguments}'. Exit code: {ExitCode}",
                    proc.Arguments,
                    proc.ExitCode);
                throw e;
            }

            tc.TestExes = testExes;
            await testContainerPath.WriteTestContainerAsync(tc.Manifest, tc.TargetDir, tc.AdditionalTestDiscoveryArguments, tc.AdditionalTestExecutionArguments, tc.TestExecutionEnvironment, profile, tc.TestExes, ct);

            if (!tc.TestExes.Any())
            {
                _logger.LogError(
                    new EventId(7, "TestExecutablesMissing"),
                    "GetTestSuiteInfoAsync: Something is not right. No test executables found in '{TestContainerPath}'.",
                    tc.ThisPath);
            }

            return tc.TestExes.Select(async exe => await GetTestSuiteInfoFromOneTestExeAsync(tc, exe, ct));
        }
        catch (Exception e)
        {
            _logger.LogError(
                new EventId(8, "TestSuiteMetadataFailed"),
                e,
                "Unable to obtain metadata for file {ManifestPath}.",
                tc.Manifest);
            throw;
        }
    }

    private BuildMessage[] OutputPreprocessorForCargoToolsWithoutJsonOutput(string msg) => new[] { new StringBuildMessage { Message = msg } };

    private async Task<TestSuiteInfo> GetTestSuiteInfoFromOneTestExeAsync(TestContainer container, PathEx testExePath, CancellationToken ct)
    {
        var workspaceRoot = container.TargetDir.GetDirectoryName();
        using var proc = await ProcessRunner.RunWithLogging(
            workspaceRoot + testExePath,
            new[] { "--list", "--format", "json", "-Zunstable-options" },
            workspaceRoot,
            ImmutableDictionary<string, string>.Empty,
            ct,
            _processLogger);

        var tests = Enumerable.Empty<TestSuiteInfo.TestInfo>();
        if (!proc.StandardOutputLines.FirstOrDefault()?.Trim()?.StartsWith("{") ?? false)
        {
            _logger.LogError(
                new EventId(9, "NightlyToolchainRequired"),
                "{ExtensionName} requires nightly toolchain. Please install the nightly toolchain following instructions in https://rust-lang.github.io/rustup/concepts/channels.html. Details: Fix for https://github.com/rust-lang/rust/issues/49359 is required to support unit testing experience. The RFC process is currently underway. Till then the fix is available only in nightly toolchain.",
                Vsix.Name);
        }
        else
        {
            tests = proc.StandardOutputLines
                .Skip(1)
                .Take(proc.StandardOutputLines.Count() - 2)
                .Select(l => DeserializeTest(workspaceRoot, l))
                .OrderBy(x => x.FQN).ThenBy(x => x.StartLine);
        }

        return new TestSuiteInfo
        {
            Container = container,
            Exe = testExePath,
            Tests = new Collection<TestSuiteInfo.TestInfo>(tests.ToList()),
        };
    }

    private static TestSuiteInfo.TestInfo DeserializeTest(PathEx workspaceRoot, string serializedVal)
    {
        var test = JsonConvert.DeserializeObject<TestSuiteInfo.TestInfo>(serializedVal);
        test.SourcePath = workspaceRoot + test.SourcePath;

        return test;
    }

    private static Workspace AddRootPackageIfNecessary(Workspace w, PathEx manifestPath)
    {
        var p = w.Packages.FirstOrDefault(p => p.ManifestPath.GetFullPath() == manifestPath.GetFullPath());
        if (p == null)
        {
            // NOTE: Means this is the root Workspace Cargo.toml that is not a package.
            var p1 =
                new Workspace.Package
                {
                    ManifestPath = manifestPath,
                    Name = Workspace.Package.RootPackageName,
                };
            w.Packages.Add(p1);
        }

        return w;
    }

    private async Task<bool> ExecuteOperationAsync(
        UsageOperation operation,
        string opName,
        PathEx filePath,
        string arguments,
        IBuildOutputSink outputPane,
        Func<BuildMessage, Task> buildMessageReporter,
        Func<string, BuildMessage[]> outputPreprocessor,
        CancellationToken ct)
    {
        return await TrackOperationAsync(
            operation,
            ct,
            async () =>
            {
                outputPane.Clear();
                var cargoFullPath = GetCargoExePath();
                return await RunAsync(
                    cargoFullPath,
                    opName,
                    arguments,
                    filePath.GetDirectoryName(),
                    redirector: new BuildOutputRedirector(outputPane, (PathEx)Path.GetDirectoryName(filePath), buildMessageReporter, outputPreprocessor),
                    ct: ct);
            });
    }

    private async Task<bool> TrackOperationAsync(
        UsageOperation operation,
        CancellationToken ct,
        Func<Task<bool>> execute)
    {
        var duration = Stopwatch.StartNew();
        try
        {
            var succeeded = await execute();
            var outcome = ct.IsCancellationRequested
                ? UsageOutcome.Cancelled
                : succeeded ? UsageOutcome.Succeeded : UsageOutcome.Failed;
            _telemetry.Track(operation, outcome, duration.Elapsed);
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            _telemetry.Track(operation, UsageOutcome.Cancelled, duration.Elapsed);
            throw;
        }
        catch (Exception)
        {
            _telemetry.Track(operation, UsageOutcome.Failed, duration.Elapsed);
            throw;
        }
    }

    private static async Task<bool> RunAsync(PathEx exeFullPath, string opName, string arguments, PathEx workingDir, ProcessOutputRedirector redirector, CancellationToken ct)
    {
        EnsureArg.IsNotEmptyOrWhiteSpace(arguments, nameof(arguments));

        var cargoVersion = await ToolchainServiceExtensions.GetCommandOutputSingleLine("cargo", "--version", workingDir, ct);
        var toolVersion = await ToolchainServiceExtensions.GetCommandOutputSingleLine(opName, "--version", workingDir, ct);

        redirector?.WriteLineWithoutProcessing($"");
        redirector?.WriteLineWithoutProcessing($"==== Build step: Started ====");
        redirector?.WriteLineWithoutProcessing($"        Using : {cargoVersion}");
        redirector?.WriteLineWithoutProcessing($"        Using : {toolVersion}");
        redirector?.WriteLineWithoutProcessing($"         Path : {exeFullPath}");
        redirector?.WriteLineWithoutProcessing($"    Arguments : {arguments}");
        redirector?.WriteLineWithoutProcessing($"   WorkingDir : {workingDir}");
        redirector?.WriteLineWithoutProcessing($"");

        return await exeFullPath.RunAsync(
            arguments,
            workingDir,
            redirector,
            finishedMsg: "==== Build step: Finished ====\n",
            cancelledMsg: "====  Build step canceled ====\n",
            ct);
    }
}

public sealed class BuildOutputRedirector : ProcessOutputRedirector
{
    private readonly IBuildOutputSink _outputPane;
    private readonly PathEx _rootPath;
    private readonly Func<BuildMessage, Task> _buildMessageReporter;
    private readonly Func<string, BuildMessage[]> _jsonProcessor;

    public BuildOutputRedirector(IBuildOutputSink outputPane, PathEx rootPath, Func<BuildMessage, Task> buildMessageReporter, Func<string, BuildMessage[]> jsonProcessor)
    {
        _outputPane = outputPane;
        _rootPath = rootPath;
        _buildMessageReporter = buildMessageReporter;
        _jsonProcessor = jsonProcessor;
    }

    public override void WriteErrorLine(string line)
    {
        WriteErrorLineWithoutProcessing(line);
    }

    public override void WriteErrorLineWithoutProcessing(string line)
    {
        WriteLineCore(line, x => new[] { new StringBuildMessage { Message = x } });
    }

    public override void WriteLine(string line)
    {
        WriteLineCore(line, _jsonProcessor);
    }

    public override void WriteLineWithoutProcessing(string line)
    {
        WriteLineCore(line, x => new[] { new StringBuildMessage { Message = x } });
    }

    private void WriteLineCore(string jsonLine, Func<string, BuildMessage[]> jsonProcessor)
    {
        var lines = jsonProcessor(jsonLine);
        Array.ForEach(
            lines,
            l =>
            {
                _outputPane.WriteLine(_rootPath, _buildMessageReporter, l);
            });
    }
}
