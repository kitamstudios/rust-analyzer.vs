using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[Export(typeof(ICargoService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CargoService : ICargoService
{
    private readonly TL _tl;

    [ImportingConstructor]
    public CargoService([Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public string GetCargoExePath()
    {
        return Constants.CargoExe.SearchInPath();
    }

    public Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            "build",
            bti.FilePath,
            arguments: $"build --manifest-path \"{bti.FilePath}\" {bti.AdditionalBuildArgs} --profile {bti.Profile} --message-format json",
            profile: bti.Profile,
            showMessageBox: bos.ShowMessageBox,
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
            bti.FilePath,
            arguments: $"clean --manifest-path \"{bti.FilePath}\" --profile {bti.Profile}",
            profile: bti.Profile,
            showMessageBox: bos.ShowMessageBox,
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

        _tl.T.TrackEvent("GetWorkspaceAsync", ("FilePath", manifestPath));
        try
        {
            using var proc = ProcessRunner.Run(cargoFullPath, new[] { "metadata", "--no-deps", "--format-version", "1", "--manifest-path", manifestPath, "--offline" }, ct);
            var exitCode = await proc;
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"{exitCode}\n{string.Join("\n", proc.StandardErrorLines)}");
            }

            return JsonConvert.DeserializeObject<Workspace>(string.Join(string.Empty, proc.StandardOutputLines));
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Unable to obtain metadata for file {0}. Ex: {1}", manifestPath, e);
            _tl.T.TrackException(e);
            throw;
        }
    }

    private async Task<bool> ExecuteOperationAsync(string opName, string filePath, string arguments, string profile, Func<string, Task> showMessageBox, IBuildOutputSink outputPane, Func<BuildMessage, Task> buildMessageReporter, Func<string, BuildMessage[]> outputPreprocessor, ITelemetryService ts, ILogger l, CancellationToken ct)
    {
        outputPane.Clear();

        var cargoFullPath = GetCargoExePath();

        ts.TrackEvent(
            opName,
            new[] { ("FilePath", filePath), ("Profile", profile), ("CargoPath", cargoFullPath), ("Arguments", arguments) });

        if (string.IsNullOrEmpty(cargoFullPath))
        {
            l.WriteLine($"{Constants.CargoExe} not found in path.");
            await showMessageBox($"Unable to perform '{opName}'.\r\n\r\n{Constants.CargoExe} is not found in path.\r\n\r\nInstall from https://www.rust-lang.org/tools/install and try again.");
            return false;
        }

        l.WriteLine("Using {0} from {1}.", Constants.CargoExe, cargoFullPath);
        return await RunAsync(
            cargoFullPath,
            arguments,
            workingDir: Path.GetDirectoryName(filePath),
            redirector: new BuildOutputRedirector(outputPane, buildMessageReporter, outputPreprocessor),
            ct: ct);
    }

    private static async Task<bool> RunAsync(string cargoFullPath, string arguments, string workingDir, ProcessOutputRedirector redirector, CancellationToken ct)
    {
        Debug.Assert(!string.IsNullOrEmpty(arguments), $"{nameof(arguments)} should not be empty.");

        redirector?.WriteLineWithoutProcessing($"\n=== Cargo started: {Constants.CargoExe} {arguments} ===");
        redirector?.WriteLineWithoutProcessing($"         Path: {cargoFullPath}");
        redirector?.WriteLineWithoutProcessing($"    Arguments: {arguments}");
        redirector?.WriteLineWithoutProcessing($"   WorkingDir: {workingDir}");
        redirector?.WriteLineWithoutProcessing($"");

        using (var process = ProcessRunner.Run(
            cargoFullPath,
            new[] { arguments },
            workingDir,
            env: null,
            visible: false,
            redirector: redirector,
            quoteArgs: false,
            outputEncoding: Encoding.UTF8,
            cancellationToken: ct))
        {
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
    }

    private sealed class BuildOutputRedirector : ProcessOutputRedirector
    {
        private readonly IBuildOutputSink _outputPane;
        private readonly Func<BuildMessage, Task> _buildMessageReporter;
        private readonly Func<string, BuildMessage[]> _jsonProcessor;

        public BuildOutputRedirector(IBuildOutputSink outputPane, Func<BuildMessage, Task> buildMessageReporter, Func<string, BuildMessage[]> jsonProcessor)
        {
            _outputPane = outputPane;
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
                    _outputPane.WriteLine(_buildMessageReporter, l);
                });
        }
    }
}
