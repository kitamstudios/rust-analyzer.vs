using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.Infrastructure;

public interface IPrerequisiteProbe
{
    Task<Version> GetVisualStudioVersionAsync(CancellationToken cancellationToken);

    string FindExecutable(string fileName);

    Task<PrerequisiteCommandResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed class PrerequisiteCommandResult
{
    private PrerequisiteCommandResult(
        bool wasStarted,
        int? exitCode,
        string standardOutput,
        string standardError,
        string startError)
    {
        WasStarted = wasStarted;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        StartError = startError;
    }

    public bool WasStarted { get; }

    public int? ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public string StartError { get; }

    public bool IsSuccess => WasStarted && ExitCode == 0;

    public static PrerequisiteCommandResult Completed(int exitCode, string standardOutput, string standardError)
    {
        return new PrerequisiteCommandResult(
            true,
            exitCode,
            standardOutput ?? throw new ArgumentNullException(nameof(standardOutput)),
            standardError ?? throw new ArgumentNullException(nameof(standardError)),
            string.Empty);
    }

    public static PrerequisiteCommandResult FailedToStart(string error)
    {
        return new PrerequisiteCommandResult(
            false,
            null,
            string.Empty,
            string.Empty,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}

public sealed class VisualStudioPrerequisiteProbe : IPrerequisiteProbe
{
    private readonly string _processPath;

    public VisualStudioPrerequisiteProbe()
        : this(Environment.GetEnvironmentVariable("PATH"))
    {
    }

    public VisualStudioPrerequisiteProbe(string processPath)
    {
        _processPath = processPath ?? string.Empty;
    }

    public async Task<Version> GetVisualStudioVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = await CommunityVS.Shell.GetVsVersionAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return version;
    }

    public string FindExecutable(string fileName)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        foreach (var pathEntry in _processPath.Split(Path.PathSeparator))
        {
            var directory = pathEntry.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }
            catch (PathTooLongException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<PrerequisiteCommandResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (executablePath == null)
        {
            throw new ArgumentNullException(nameof(executablePath));
        }

        if (arguments == null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = ProcessRunner.GetArguments(arguments, quoteArgs: true),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.EnvironmentVariables["RUSTUP_AUTO_INSTALL"] = "0";

        try
        {
            if (!process.Start())
            {
                cancellationToken.ThrowIfCancellationRequested();
                return PrerequisiteCommandResult.FailedToStart("The process API did not start the command.");
            }
        }
        catch (Win32Exception e)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PrerequisiteCommandResult.FailedToStart(e.Message);
        }
        catch (InvalidOperationException e)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PrerequisiteCommandResult.FailedToStart(e.Message);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var cancellationRegistration = cancellationToken.Register(() => KillForCancellation(process));

        await Task.Run(() => process.WaitForExit());
        var output = await standardOutput;
        var error = await standardError;
        cancellationToken.ThrowIfCancellationRequested();

        return PrerequisiteCommandResult.Completed(process.ExitCode, output, error);
    }

    private static void KillForCancellation(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (Win32Exception) when (process.HasExited)
        {
        }
    }
}

public sealed class PrerequisiteEvaluator
{
    private const string DefaultMarker = " (default)";
    private readonly IPrerequisiteProbe _probe;

    public PrerequisiteEvaluator(IPrerequisiteProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public static bool IsSupportedVisualStudioVersion(Version version)
    {
        return version != null &&
            ((version.Major == 17 && version.CompareTo(Constants.MinimumRequiredVsVersion) >= 0) ||
            version.Major == 18);
    }

    public async Task<PrerequisiteResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failures = new List<PrerequisiteFailure>();

        var visualStudioVersion = await _probe.GetVisualStudioVersionAsync(cancellationToken);
        if (!IsSupportedVisualStudioVersion(visualStudioVersion))
        {
            var message = visualStudioVersion == null
                ? "Visual Studio version could not be detected. Repair or update Visual Studio, then restart Visual Studio."
                : $"Visual Studio {visualStudioVersion} is unsupported. Install Visual Studio 2022 17.12 or a later 17.x release, or Visual Studio 2026 18.x, then restart Visual Studio.";
            failures.Add(
                new PrerequisiteFailure(
                    PrerequisiteFailureKind.UnsupportedVisualStudioHost,
                    message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var rustupPath = _probe.FindExecutable(Constants.RustUpExe);
        cancellationToken.ThrowIfCancellationRequested();
        var cargoPath = _probe.FindExecutable(Constants.CargoExe);
        cancellationToken.ThrowIfCancellationRequested();

        string defaultToolchain = null;
        if (rustupPath == null)
        {
            failures.Add(
                new PrerequisiteFailure(
                    PrerequisiteFailureKind.RustupNotFound,
                    "rustup.exe is not on the Visual Studio process PATH. Install rustup or add its directory to PATH, then restart Visual Studio."));
        }
        else
        {
            var rustupVersion = await _probe.RunAsync(rustupPath, new[] { "--version" }, cancellationToken);
            if (!rustupVersion.IsSuccess)
            {
                failures.Add(
                    new PrerequisiteFailure(
                        PrerequisiteFailureKind.RustupNotOperational,
                        $"rustup.exe was found but could not run successfully ({DescribeFailure(rustupVersion)}). Repair rustup, then restart Visual Studio."));
            }
            else
            {
                var rustupDefault = await _probe.RunAsync(rustupPath, new[] { "default" }, cancellationToken);
                if (!rustupDefault.WasStarted)
                {
                    failures.Add(
                        new PrerequisiteFailure(
                            PrerequisiteFailureKind.RustupNotOperational,
                            $"rustup.exe was found but could not query its default toolchain ({DescribeFailure(rustupDefault)}). Repair rustup, then restart Visual Studio."));
                }
                else
                {
                    defaultToolchain = rustupDefault.IsSuccess
                        ? ParseDefaultToolchain(rustupDefault.StandardOutput)
                        : null;
                    if (defaultToolchain == null)
                    {
                        failures.Add(
                            new PrerequisiteFailure(
                                PrerequisiteFailureKind.DefaultToolchainNotConfigured,
                                "rustup has no configured default Rust toolchain. Run 'rustup default stable', then restart Visual Studio."));
                    }
                }
            }
        }

        if (cargoPath == null)
        {
            failures.Add(
                new PrerequisiteFailure(
                    PrerequisiteFailureKind.CargoNotFound,
                    "cargo.exe is not on the Visual Studio process PATH. Install Cargo for the default Rust toolchain or add its directory to PATH, then restart Visual Studio."));
        }
        else if (defaultToolchain != null)
        {
            var cargoVersion = await _probe.RunAsync(
                cargoPath,
                new[] { $"+{defaultToolchain}", "--version" },
                cancellationToken);
            if (!cargoVersion.IsSuccess)
            {
                failures.Add(
                    new PrerequisiteFailure(
                        PrerequisiteFailureKind.CargoNotOperational,
                        $"cargo.exe was found but could not run for default toolchain '{defaultToolchain}' ({DescribeFailure(cargoVersion)}). Repair the default Rust toolchain, then restart Visual Studio."));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return failures.Count == 0
            ? PrerequisiteResult.Success
            : PrerequisiteResult.Failed(failures);
    }

    private static string ParseDefaultToolchain(string standardOutput)
    {
        var line = standardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .FirstOrDefault();
        if (line == null)
        {
            return null;
        }

        if (line.EndsWith(DefaultMarker, StringComparison.OrdinalIgnoreCase))
        {
            line = line.Substring(0, line.Length - DefaultMarker.Length);
        }

        return line.Length == 0 || line.Any(char.IsWhiteSpace) ? null : line;
    }

    private static string DescribeFailure(PrerequisiteCommandResult result)
    {
        return result.WasStarted
            ? $"exit code {result.ExitCode}"
            : $"start failed: {result.StartError}";
    }
}
