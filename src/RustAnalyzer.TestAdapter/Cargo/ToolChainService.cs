using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[Export(typeof(IToolChainService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ToolChainService : IToolChainService
{
    private static readonly Regex TestExecutablePathCracker = new (@"^\s*Executable( unittests)? (.*) \((.*)\)$$", RegexOptions.Compiled);

    private readonly TL _tl;

    [ImportingConstructor]
    public ToolChainService([Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public PathEx? GetCargoExePath()
    {
        PathEx? cargoExePath = (PathEx)Environment.GetEnvironmentVariable("USERPROFILE") + (PathEx)@".cargo\bin" + Constants.CargoExe2;
        if (!cargoExePath.Value.FileExists())
        {
            cargoExePath = (PathEx?)Constants.CargoExe.SearchInPath();
        }

        _tl.L.WriteLine("... using {0} from '{1}'.", Constants.CargoExe, cargoExePath);
        return cargoExePath;
    }

    public Task<PathEx> GetRustAnalyzerExePath()
    {
        var path = (PathEx)Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rust-analyzer.exe");
        return path.ToTask();
    }

    public async Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        var success = await ExecuteOperationAsync(
            "build",
            bti.ManifestPath,
            arguments: $"build --manifest-path \"{bti.ManifestPath}\" --profile {bti.Profile} --message-format json {bti.AdditionalBuildArgs}",
            profile: bti.Profile,
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: x => BuildJsonOutputParser.Parse(bti.WorkspaceRoot, x, _tl),
            ts: _tl.T,
            l: _tl.L,
            ct: ct);

        if (success)
        {
            var w = await GetWorkspaceAsync(bti.ManifestPath, ct);
            var tasks = w.Packages
                .SelectMany(p => p.GetTestContainers(bti.Profile))
                .Select(x => x.Container.WriteTestContainerAsync(x.Target.Parent.ManifestPath, w.TargetDirectory, x.Target.SourcePath, bti.AdditionalTestDiscoveryArguments, bti.AdditionalTestExecutionArguments, bti.TestExecutionEnvironment, bti.Profile, null, ct));
            await Task.WhenAll(tasks);
        }

        return success;
    }

    public Task<bool> CleanAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            "clean",
            bti.ManifestPath,
            arguments: $"clean --manifest-path \"{bti.ManifestPath}\" --profile {bti.Profile}",
            profile: bti.Profile,
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: x => new[] { new StringBuildMessage { Message = x } },
            ts: _tl.T,
            l: _tl.L,
            ct: ct);
    }

    public Task<bool> RunClippyAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            "Clippy",
            bti.ManifestPath,
            arguments: $"clippy --manifest-path \"{bti.ManifestPath}\" --profile {bti.Profile} {bti.AdditionalBuildArgs}",
            profile: bti.Profile,
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: x => new[] { new StringBuildMessage { Message = x } },
            ts: _tl.T,
            l: _tl.L,
            ct: ct);
    }

    public Task<bool> RunFmtAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            "Fmt",
            bti.ManifestPath,
            arguments: $"fmt --manifest-path \"{bti.ManifestPath}\" {bti.AdditionalBuildArgs}",
            profile: bti.Profile,
            outputPane: bos.OutputSink,
            buildMessageReporter: bos.BuildActionProgressReporter,
            outputPreprocessor: x => new[] { new StringBuildMessage { Message = x } },
            ts: _tl.T,
            l: _tl.L,
            ct: ct);
    }

    // TODO: NEW: "Build all" enabled if top level cargo.toml exists.
    public async Task<Workspace> GetWorkspaceAsync(PathEx manifestPath, CancellationToken ct)
    {
        var cargoFullPath = GetCargoExePath();

        var exitCode = 0;
        try
        {
            using var proc = await ProcessRunner.RunWithLogging(
                cargoFullPath,
                new[] { "metadata", "--no-deps", "--format-version", "1", "--manifest-path", manifestPath, "--offline" },
                cargoFullPath?.GetDirectoryName(),
                ImmutableDictionary<string, string>.Empty,
                ct,
                _tl.L);
            exitCode = proc.ExitCode ?? 0;
            var w = JsonConvert.DeserializeObject<Workspace>(string.Join(string.Empty, proc.StandardOutputLines));
            return AddRootPackageIfNecessary(w, manifestPath);
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Unable to obtain metadata for file {0}. Ex: {1}", manifestPath, e);
            if (exitCode != 101)
            {
                // TODO: 2. RELEASE: This wont work.
                _tl.T.TrackException(e);
            }

            throw;
        }
    }

    public async Task<TestSuiteInfo> GetTestSuiteInfoAsync(PathEx testContainerPath, string profile, CancellationToken ct)
    {
        var cargoFullPath = GetCargoExePath();
        var tc = await testContainerPath.ReadTestContainerAsync(ct);

        var exitCode = 0;
        try
        {
            var args = new[] { "test", "--no-run", "--manifest-path", tc.Manifest, "--profile", profile }
                .Concat(tc.AdditionalTestDiscoveryArguments.ToNullSeparatedArray())
            .ToArray();

            _tl.T.TrackEvent("GetTestSuiteInfoAsync", ("TestContainer", testContainerPath), ("Profile", profile), ("Args", string.Join("|", args)));

            using var proc = await ProcessRunner.RunWithLogging(cargoFullPath, args, cargoFullPath?.GetDirectoryName(), ImmutableDictionary<string, string>.Empty, ct, _tl.L);
            exitCode = proc.ExitCode ?? 0;

            var testExeBuildInfos = proc.StandardErrorLines
                .Select(l => TestExecutablePathCracker.Matches(l))
                .Where(m => m.Count > 0 && m[0].Groups.Count == 4)
                .Select(m => (source: tc.Manifest.GetDirectoryName() + (PathEx)m[0].Groups[2].Value, testExe: (PathEx)m[0].Groups[3].Value))
                .Where(x => x.source == tc.Source);
            if (!testExeBuildInfos.Any())
            {
                var e = new InvalidOperationException(string.Format("Unable to parse output of cargo test to obtain test exe paths. Command line '{0}'. Exit code: {1}", proc.Arguments, proc.ExitCode));
                _tl.L.WriteError(e.Message);
                _tl.T.TrackException(e);
                throw e;
            }

            // NOTE: We are gauranteed to have only 1 exe here even if there are multiple files with tests. See workspace_with_tests/adder.
            tc.TestExe = testExeBuildInfos.First().testExe;
            await testContainerPath.WriteTestContainerAsync(tc.Manifest, tc.TargetDir, tc.Source, tc.AdditionalTestDiscoveryArguments, tc.AdditionalTestExecutionArguments, tc.TestExecutionEnvironment, profile, tc.TestExe, ct);

            return await GetTestSuiteInfoFromOneTestExe(tc.TestExe, testContainerPath, tc.TargetDir.GetDirectoryName(), ct);
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Unable to obtain metadata for file {0}. Ex: {1}", tc.Manifest, e);
            if (exitCode != 101)
            {
                _tl.T.TrackException(e);
            }

            throw;
        }
    }

    private async Task<TestSuiteInfo> GetTestSuiteInfoFromOneTestExe(PathEx testExePath, PathEx testContainerPath, PathEx workspaceRoot, CancellationToken ct)
    {
        using var proc = await ProcessRunner.RunWithLogging(testExePath, new[] { "--list", "--format", "json", "-Zunstable-options" }, workspaceRoot, ImmutableDictionary<string, string>.Empty, ct, _tl.L);
        var testSuites = proc.StandardOutputLines
            .Skip(1)
            .Take(proc.StandardOutputLines.Count() - 2)
            .Select(l => DeserializeTest(workspaceRoot, l))
            .OrderBy(x => x.FQN).ThenBy(x => x.StartLine)
            .ToList();

        return new TestSuiteInfo
        {
            Container = testContainerPath,
            Tests = new Collection<TestSuiteInfo.TestInfo>(testSuites),
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

    private async Task<bool> ExecuteOperationAsync(string opName, string filePath, string arguments, string profile, IBuildOutputSink outputPane, Func<BuildMessage, Task> buildMessageReporter, Func<string, BuildMessage[]> outputPreprocessor, ITelemetryService ts, ILogger l, CancellationToken ct)
    {
        outputPane.Clear();

        var cargoFullPath = GetCargoExePath();

        ts.TrackEvent(
            opName,
            new[] { ("FilePath", filePath), ("Profile", profile), ("CargoPath", cargoFullPath.Value), ("Arguments", arguments) });

        return await RunAsync(
            cargoFullPath.Value,
            arguments,
            cargoFullPath?.GetDirectoryName(),
            redirector: new BuildOutputRedirector(outputPane, (PathEx)Path.GetDirectoryName(filePath), buildMessageReporter, outputPreprocessor),
            ct: ct);
    }

    private static async Task<bool> RunAsync(PathEx cargoFullPath, string arguments, string workingDir, ProcessOutputRedirector redirector, CancellationToken ct)
    {
        EnsureArg.IsNotEmptyOrWhiteSpace(arguments, nameof(arguments));

        redirector?.WriteLineWithoutProcessing($"\n=== Cargo started: {Constants.CargoExe} {arguments} ===");
        redirector?.WriteLineWithoutProcessing($"         Path: {cargoFullPath}");
        redirector?.WriteLineWithoutProcessing($"    Arguments: {arguments}");
        redirector?.WriteLineWithoutProcessing($"   WorkingDir: {workingDir}");
        redirector?.WriteLineWithoutProcessing($"");

        using var process = ProcessRunner.Run(
            cargoFullPath,
            new[] { arguments },
            workingDir,
            env: null,
            visible: false,
            redirector: redirector,
            quoteArgs: false,
            outputEncoding: Encoding.UTF8,
            cancellationToken: ct);
        var whnd = process.WaitHandle;
        if (whnd == null)
        {
            // Process failed to start, and any exception message has
            // already been sent through the redirector
            redirector.WriteErrorLineWithoutProcessing(string.Format("Error - Failed to start '{0}'", cargoFullPath));
            return false;
        }
        else
        {
            var finished = await Task.Run(() => whnd.WaitOne());
            if (finished)
            {
                Debug.Assert(process.ExitCode.HasValue, "cargo.exe process has not really exited");

                // there seems to be a case when we're signalled as completed, but the
                // process hasn't actually exited
                process.Wait();

                redirector.WriteLineWithoutProcessing($"==== Cargo completed ====");

                return process.ExitCode == 0;
            }
            else
            {
                process.Kill();
                redirector.WriteErrorLineWithoutProcessing($"====  Cargo canceled ====");

                return false;
            }
        }
    }

    private sealed class BuildOutputRedirector : ProcessOutputRedirector
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
}
