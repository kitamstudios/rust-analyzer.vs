using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.Cargo;

public class ExeRunner
{
    public static Task<bool> BuildAsync(string workspaceRoot, string filePath, string profile, IOutputWindowPane outputPane, Func<BuildMessage, object, Task> buildMessageReporter, ITelemetryService ts, Func<string, Task> showMessageBox, ILogger l, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            "build",
            filePath,
            arguments: $"build --manifest-path \"{filePath}\" --profile {profile} --message-format json",
            profile,
            ts,
            showMessageBox,
            outputPane,
            buildMessageReporter,
            l,
            x => BuildJsonOutputParser.Parse(workspaceRoot, x, l, ts),
            ct);
    }

    public static Task<bool> CleanAsync(string workspaceRoot, string filePath, string profile, IOutputWindowPane outputPane, Func<BuildMessage, object, Task> buildMessageReporter, ITelemetryService ts, Func<string, Task> showMessageBox, ILogger l, CancellationToken ct)
    {
        return ExecuteOperationAsync(
            "clean",
            filePath,
            arguments: $"clean --manifest-path \"{filePath}\" --profile {profile}",
            profile,
            ts,
            showMessageBox,
            outputPane,
            buildMessageReporter,
            l,
            x => new[] { new StringOutputMessage { Message = x } },
            ct);
    }

    private static async Task<bool> ExecuteOperationAsync(string opName, string filePath, string arguments, string profile, ITelemetryService ts, Func<string, Task> showMessageBox, IOutputWindowPane outputPane, Func<BuildMessage, object, Task> buildMessageReporter, ILogger l, Func<string, OutputMessage[]> outputPreprocessor, CancellationToken ct)
    {
        if (!Manifest.IsManifest(filePath) || !Path.IsPathRooted(filePath) || true)
        {
            l.WriteLine("{0} has to be a rooted cargo file", filePath);
        }

        outputPane.Clear();

        var cargoFullPath = PathUtilities.SearchInPath(Constants.CargoExe);

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

        using (var process = ProcessOutputProcessor.Run(
            cargoFullPath,
            new[] { arguments },
            workingDir,
            env: null,
            visible: false,
            redirector: redirector,
            quoteArgs: false,
            outputEncoding: Encoding.UTF8))
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
        private readonly IOutputWindowPane _outputPane;
        private readonly Func<BuildMessage, object, Task> _buildMessageReporter;
        private readonly Func<string, OutputMessage[]> _jsonProcessor;

        public BuildOutputRedirector(IOutputWindowPane outputPane, Func<BuildMessage, object, Task> buildMessageReporter, Func<string, OutputMessage[]> jsonProcessor)
        {
            _outputPane = outputPane;
            _buildMessageReporter = buildMessageReporter;
            _jsonProcessor = jsonProcessor;
        }

        public override void WriteErrorLine(string line)
        {
            WriteLineCore(line, _jsonProcessor);
        }

        public override void WriteErrorLineWithoutProcessing(string line)
        {
            WriteLineCore(line, x => new[] { new StringOutputMessage { Message = x } });
        }

        public override void WriteLine(string line)
        {
            WriteLineCore(line, _jsonProcessor);
        }

        public override void WriteLineWithoutProcessing(string line)
        {
            WriteLineCore(line, x => new[] { new StringOutputMessage { Message = x } });
        }

        private void WriteLineCore(string jsonLine, Func<string, OutputMessage[]> jsonProcessor)
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