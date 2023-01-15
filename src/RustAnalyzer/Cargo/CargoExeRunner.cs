using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KS.RustAnalyzer.Common;
using KS.RustAnalyzer.VS;

namespace KS.RustAnalyzer.Cargo;

public class CargoExeRunner
{
    public static Task<bool> BuildAsync(string filePath, string profile, RustOutputPane outputPane, ITelemetryService ts, Func<string, Task> showMessageBox)
    {
        return ExecuteOperationAsync(
            "build",
            filePath,
            arguments: $"build --manifest-path \"{filePath}\" --profile {profile} --message-format json",
            profile,
            ts,
            showMessageBox,
            outputPane,
            CargoJsonOutputParser.Parse);
    }

    public static Task<bool> CleanAsync(string filePath, string profile, RustOutputPane outputPane, ITelemetryService ts, Func<string, Task> showMessageBox)
    {
        return ExecuteOperationAsync(
            "clean",
            filePath,
            arguments: $"clean --manifest-path \"{filePath}\" --profile {profile}",
            profile,
            ts,
            showMessageBox,
            outputPane,
            x => new[] { x });
    }

    private static async Task<bool> ExecuteOperationAsync(string opName, string filePath, string arguments, string profile, ITelemetryService ts, Func<string, Task> showMessageBox, RustOutputPane outputPane, Func<string, string[]> outputPreprocessor)
    {
        if (!RustHelpers.IsCargoFile(filePath) || !Path.IsPathRooted(filePath))
        {
            // TODO: Log this.
            throw new ArgumentException($"{nameof(filePath)} has to be a rooted cargo file.");
        }

        outputPane.Clear();

        var cargoFullPath = PathUtilities.SearchInPath(RustConstants.CargoExe);

        ts.TrackEvent(
            opName,
            new[] { ("FilePath", filePath), ("Profile", profile), ("CargoPath", cargoFullPath), ("Arguments", arguments) });

        if (string.IsNullOrEmpty(cargoFullPath))
        {
            await showMessageBox($"Unable to perform '{opName}'.\r\n\r\n{RustConstants.CargoExe} is not found in path.\r\n\r\nInstall from https://www.rust-lang.org/tools/install and try again.");
            return false;
        }

        return await RunAsync(
            cargoFullPath,
            arguments,
            workingDir: Path.GetDirectoryName(filePath),
            redirector: new BuildOutputRedirector(outputPane, outputPreprocessor));
    }

    private static async Task<bool> RunAsync(string cargoFullPath, string arguments, string workingDir, ProcessOutputRedirector redirector)
    {
        Debug.Assert(!string.IsNullOrEmpty(arguments), $"{nameof(arguments)} should not be empty.");

        redirector?.WriteLineWithoutProcessing($"\n=== Cargo started: {RustConstants.CargoExe} {arguments} ===");
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
        private readonly RustOutputPane _outputPane;
        private readonly Func<string, string[]> _jsonProcessor;

        public BuildOutputRedirector(RustOutputPane outputPane, Func<string, string[]> jsonProcessor)
        {
            _outputPane = outputPane;
            _jsonProcessor = jsonProcessor;
        }

        public override void WriteErrorLine(string line)
        {
            WriteLineCore(line, _outputPane, _jsonProcessor);
        }

        public override void WriteErrorLineWithoutProcessing(string line)
        {
            WriteLineCore(line, _outputPane, x => new[] { x });
        }

        public override void WriteLine(string line)
        {
            WriteLineCore(line, _outputPane, _jsonProcessor);
        }

        public override void WriteLineWithoutProcessing(string line)
        {
            WriteLineCore(line, _outputPane, x => new[] { x });
        }

        private static void WriteLineCore(string jsonLine, RustOutputPane outputPane, Func<string, string[]> jsonProcessor)
        {
            var lines = jsonProcessor(jsonLine);
            Array.ForEach(
                lines,
                l =>
                {
                    if (!string.IsNullOrEmpty(l))
                    {
                        outputPane.WriteLine(l, OutputWindowTarget.Cargo);
                    }
                });
        }
    }
}