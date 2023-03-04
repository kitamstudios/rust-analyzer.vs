using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[Export(typeof(IToolChainService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ToolChainService : IToolChainService
{
    private static readonly Regex TestExecutablePathCracker = new (@"^\s*Executable unittests (.*) \((.*)\)$", RegexOptions.Compiled);

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

    public PathEx? GetRustUpExePath()
    {
        var c = (PathEx?)Constants.RustUpExe.SearchInPath();
        _tl.L.WriteLine("... using {0} from '{1}'.", Constants.RustUpExe, c);
        return c;
    }

    public PathEx? GetCargoExePath()
    {
        var c = (PathEx?)Constants.CargoExe.SearchInPath();
        _tl.L.WriteLine("... using {0} from '{1}'.", Constants.CargoExe, c);
        return c;
    }

    public Task<PathEx> GetRustAnalyzerExePath()
    {
        var path = (PathEx)Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rust-analyzer.exe");
        return path.ToTask();
    }

    public Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
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

    public async Task<Workspace> GetWorkspaceAsync(PathEx manifestPath, CancellationToken ct)
    {
        var cargoFullPath = GetCargoExePath();

        var exitCode = 0;
        try
        {
            using var proc = ProcessRunner.Run(cargoFullPath, new[] { "metadata", "--no-deps", "--format-version", "1", "--manifest-path", manifestPath, "--offline" }, ct);
            _tl.L.WriteLine("Started PID:{0} with args: {1}...", proc.ProcessId, proc.Arguments);
            exitCode = await proc;
            _tl.L.WriteLine("... Finished PID {0} with exit code {1}.", proc.ProcessId, proc.ExitCode);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"{exitCode}\n{string.Join("\n", proc.StandardErrorLines)}");
            }

            var w = JsonConvert.DeserializeObject<Workspace>(string.Join(string.Empty, proc.StandardOutputLines));
            return AddRootPackageIfNecessary(w, manifestPath);
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Unable to obtain metadata for file {0}. Ex: {1}", manifestPath, e);
            if (exitCode != 101)
            {
                _tl.T.TrackException(e);
            }

            throw;
        }
    }

    public async Task<IEnumerable<TestInfo>> GetTestSuiteAsync(PathEx manifestPath, CancellationToken ct)
    {
        var cargoFullPath = GetCargoExePath();

        var exitCode = 0;
        try
        {
            using var proc = ProcessRunner.Run(cargoFullPath, new[] { "test", "--no-run", "--manifest-path", manifestPath }, ct);
            _tl.L.WriteLine("Started PID:{0} with args: {1}...", proc.ProcessId, proc.Arguments);
            exitCode = await proc;
            _tl.L.WriteLine("... Finished PID {0} with exit code {1}.", proc.ProcessId, proc.ExitCode);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"{exitCode}\n{string.Join("\n", proc.StandardErrorLines)}");
            }

            // TODO: this assumes there will be only one testexe listed.
            var lastLine = proc.StandardErrorLines.Last(StringExtensions.IsNotNullOrEmpty);
            var matches = TestExecutablePathCracker.Matches(lastLine);
            if (!(matches.Count > 0 && matches[0].Groups.Count == 3))
            {
                throw new InvalidOperationException($"Couldn't extract the test exe name. Invalid format of last line: {lastLine}");
            }

            var testExePath = matches[0].Groups[2].Value;
            using var testExeProc = ProcessRunner.Run(testExePath, new[] { "--list", "--format", "json", "-Zunstable-options" }, ct);
            _tl.L.WriteLine("Started PID:{0}, with args: {1}...", testExeProc.ProcessId, testExeProc.Arguments);
            var testExeExitCode = await testExeProc;
            _tl.L.WriteLine("... Finished PID {0} with exit code {1}.", testExeProc.ProcessId, testExeProc.ExitCode);
            if (testExeExitCode != 0)
            {
                throw new InvalidOperationException($"{testExeExitCode}\n{string.Join("\n", testExeProc.StandardErrorLines)}");
            }

            var manifestLoc = manifestPath.GetDirectoryName();
            var testSuites = testExeProc.StandardOutputLines
                .Skip(1)
                .Take(testExeProc.StandardOutputLines.Count() - 2)
                .Select(l => DeserializeTest(manifestLoc, l));

            return testSuites;
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Unable to obtain metadata for file {0}. Ex: {1}", manifestPath, e);
            if (exitCode != 101)
            {
                _tl.T.TrackException(e);
            }

            throw;
        }
    }

    private static TestInfo DeserializeTest(PathEx manifestLocation, string serializedVal)
    {
        var test = JsonConvert.DeserializeObject<TestInfo>(serializedVal);
        test.SourcePath = manifestLocation.Combine(test.SourcePath);

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

        string workingDir = Path.GetDirectoryName(filePath);
        return await RunAsync(
            cargoFullPath.Value,
            arguments,
            workingDir,
            redirector: new BuildOutputRedirector(outputPane, (PathEx)workingDir, buildMessageReporter, outputPreprocessor),
            ct: ct);
    }

    private static async Task<bool> RunAsync(PathEx cargoFullPath, string arguments, string workingDir, ProcessOutputRedirector redirector, CancellationToken ct)
    {
        Debug.Assert(!string.IsNullOrEmpty(arguments), $"{nameof(arguments)} should not be empty.");

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
