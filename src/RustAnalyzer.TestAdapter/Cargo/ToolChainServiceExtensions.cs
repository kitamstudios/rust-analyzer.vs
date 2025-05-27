using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

/// <summary>
/// Following files not in path is a catastrophic error.
/// - rustup.exe
/// - cargo.exe
///
/// Hence they are in prereq checks.
/// </summary>
public static class ToolchainServiceExtensions
{
    public const string AlwaysAvailableTarget = "x86_64-pc-windows-msvc";

    public static readonly string[] CommonTargets = new[]
    {
        "wasm32-unknown-unknown",
    };

    private static readonly Regex NameCracker =
        new(@"^((?<name>.*)(?<default> \((active, )?default\))?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

    private static readonly Regex ToolchainVersionCracker =
        new(@"^rustc (?<version>\d+.\d+.\d+(-.*)?) (\(.* (?<date>\d{4}-\d{2}-\d{2})\))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RustupVersionCracker =
        new(@"^rustup (?<version>\d+.\d+.\d+(-.*)?) (\(.* (?<date>\d{4}-\d{2}-\d{2})\))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> OpNameToToolNameMapper = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "rustup", "rustup" },
        { "cargo", "cargo" },
        { "build", "rustc" },
        { "clean", "rustc" },
        { "test", "rustc" },
        { "fmt", "cargo-fmt" },
        { "clippy", "cargo-clippy" },
    };

    /// <summary>
    /// Not finding rustup.exe is a catastrophic error. Hence in prereq checks.
    /// </summary>
    public static PathEx GetRustupPath() =>
        (PathEx)Constants.RustUpExe.FindInPath();

    public static PathEx GetRustupSettingsPath() =>
        GetRustupPath().GetDirectoryName().GetDirectoryName().GetDirectoryName() + ".rustup" + Constants.RustupSettingsFileName;

    public static async Task<(PathEx Bin, PathEx Lib)> GetBinAndLibPathsAsync(PathEx workingDirectory, CancellationToken ct)
    {
        var root = GetRustupSettingsPath().GetDirectoryName() + @$"toolchains\{await GetDefaultToolchainAsync(workingDirectory, ct)}";
        return (root + "bin", root + $@"lib\rustlib\{AlwaysAvailableTarget}\lib");
    }

    public static async Task<Toolchain[]> GetInstalledToolchainsAsync(PathEx workingDirectory, CancellationToken ct)
    {
        var rustupVersionOutput = await GetCommandOutput("rustup", "--version", workingDirectory, ct);
        var rustupVersion = Version.Parse(RustupVersionCracker.Match(rustupVersionOutput[0]).Groups["version"].Value);

        var output = await GetCommandOutput("rustup", "show --verbose", workingDirectory, ct);
        var tcRaw = output
            .Select(x => x.Trim())
            .Where(x => !x.IsNullOrEmptyOrWhiteSpace())
            .SkipWhile(x => !x.Equals("installed toolchains"))
            .Skip(2)
            .TakeWhile(x => !x.Equals("active toolchain"))
            .ToList();

        // If the rustup version is less 1.28.2, each toolchain will have 2 lines of output.
        int linesPerToolchain = (rustupVersion >= new Version(1, 28, 2)) ? 3 : 2;

        var tcs = Enumerable.Range(0, tcRaw.Count / linesPerToolchain)
            .Select(i => (tcRaw[i * linesPerToolchain], tcRaw[(i * linesPerToolchain) + 1]))
            .Select(x => (NameCracker.Match(x.Item1), ToolchainVersionCracker.Match(x.Item2)))
            .Where(x => x.Item1.Success && x.Item2.Success)
            .Select(x =>
                new Toolchain
                {
                    Name = x.Item1.Groups["name"].Value,
                    Version = $"{x.Item2.Groups["version"].Value} ({x.Item2.Groups["date"].Value})",
                    IsDefault = false
                })
            .OrderBy(tc => tc.Name)
            .ThenBy(tc => tc.Version)
            .ToArray();

        var activeRaw = output
            .Select(x => x.Trim())
            .Where(x => !x.IsNullOrEmptyOrWhiteSpace())
            .SkipWhile(x => !x.Equals("active toolchain"))
            .Skip(2)
            .FirstOrDefault()
            .Split(' ')[1];
        if (activeRaw != null)
        {
            var i = Array.FindIndex(tcs, tc => tc.Name == activeRaw);
            if (i != -1)
            {
                tcs[i].IsDefault = true;
            }
        }

        return tcs;
    }

    public static async Task<string[]> GetTargets(CancellationToken ct)
    {
        var output = await GetCommandOutput("rustup", "target list", GetRustupPath().GetDirectoryName(), ct);
        var targets = output
            .Where(x => !x.IsNullOrEmptyOrWhiteSpace())
            .Select(x => x.Replace(" (installed)", string.Empty))
            .Where(x => x != AlwaysAvailableTarget)
            .Where(x => !CommonTargets.Contains(x))
            .Select(x => x.Trim())
            .OrderBy(t => t);

        return CommonTargets.OrderBy(x => x).Concat(targets).ToArray();
    }

    public static async Task<string> GetDefaultToolchainAsync(PathEx workingDirectory, CancellationToken ct)
    {
        return (await GetInstalledToolchainsAsync(workingDirectory, ct)).Where(x => x.IsDefault).First().Name;
    }

    public static async Task<TestContainer> ReadTestContainerAsync(this PathEx @this, CancellationToken ct)
    {
        return JsonConvert.DeserializeObject<TestContainer>(await @this.ReadAllTextAsync(ct));
    }

    public static Task WriteTestContainerAsync(this PathEx @this, PathEx manifestPath, PathEx targetPath, string additionalTestDiscoveryArguments, string additionalTestExecutionArguments, string testExecutionEnvironment, string profile, PathEx[] testExes, CancellationToken ct)
    {
        return @this.WriteAllTextAsync(
            JsonConvert.SerializeObject(
                new TestContainer
                {
                    ThisPath = @this,
                    Manifest = manifestPath,
                    TargetDir = targetPath,
                    AdditionalTestDiscoveryArguments = additionalTestDiscoveryArguments,
                    AdditionalTestExecutionArguments = additionalTestExecutionArguments,
                    TestExecutionEnvironment = testExecutionEnvironment,
                    Profile = profile,
                    TestExes = testExes,
                },
                Formatting.Indented,
                new PathExJsonConverter()),
            ct);
    }

    public static void CleanTestContainers(this PathEx @this, IEnumerable<PathEx> except)
    {
        if (!@this.DirectoryExists())
        {
            return;
        }

        Directory.EnumerateFiles(@this, $"*{Constants.TestsContainerExtension}")
            .Where(f => !except.Any(c => c == (PathEx)f))
            .ForEach(File.Delete);
    }

    public static async Task<bool> RunAsync(this PathEx exeFullPath, string args, PathEx workingDir, ProcessOutputRedirector redirector, string finishedMsg, string cancelledMsg, CancellationToken ct)
    {
        using var process = ProcessRunner.Run(
            exeFullPath,
            new[] { args },
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
            redirector.WriteErrorLineWithoutProcessing(string.Format("Error - Failed to start '{0}'", exeFullPath));
            return false;
        }
        else
        {
            var finished = await Task.Run(() => whnd.WaitOne());
            if (finished)
            {
                Debug.Assert(process.ExitCode.HasValue, "process has not really exited");

                // there seems to be a case when we're signalled as completed, but the
                // process hasn't actually exited
                process.Wait();

                redirector.WriteLineWithoutProcessing(finishedMsg);

                return process.ExitCode == 0;
            }
            else
            {
                process.Kill();
                redirector.WriteErrorLineWithoutProcessing(cancelledMsg);

                return false;
            }
        }
    }

    public static async Task SetToolchainOverrideAsync(this PathEx workspaceRoot, string toolChain, ILogger l, CancellationToken ct)
    {
        var opName = "rustup";
        var args = $"override set {toolChain}";

        l.WriteLine("Running: {0} {1}", opName, args);
        l.WriteLine("Workspace: {0}", workspaceRoot);

        var output = await GetCommandOutput(opName, args, workspaceRoot, ct);
        l.WriteLine("{0}", string.Join("\n", output));
    }

    public static async Task<string[]> GetCommandOutput(string opName, string args, PathEx workingDirectory, CancellationToken ct)
    {
        var toolName = OpNameToToolNameMapper[opName];
        using var proc = ProcessRunner.Run("cmd.exe", new[] { "/c", $"{toolName} {args}" }, workingDirectory, ImmutableDictionary<string, string>.Empty, ct);

        var ec = await proc;
        var output = proc.StandardOutputLines.Concat(proc.StandardErrorLines).ToArray();
        if (ec != 0)
        {
            return new[] { $"{toolName} returned {ec}.\nOutput: {string.Join(Environment.NewLine, output)}" };
        }

        return output;
    }

    public static async Task<string> GetCommandOutputSingleLine(string opName, string versionArgs, PathEx workingDirectory, CancellationToken ct)
    {
        var lines = await GetCommandOutput(opName, versionArgs, workingDirectory, ct);

        return string.Join(string.Empty, lines.Where(l => !l.IsNullOrEmptyOrWhiteSpace()));
    }

    public static Task<bool> InstallToolchain(string commandline, IBuildOutputSink bos, CancellationToken ct)
    {
        bos.Clear();

        var rustupPath = GetRustupPath();
        return rustupPath.RunAsync(
            commandline,
            rustupPath.GetPathRoot(),
            new BuildOutputRedirector(
                bos,
                rustupPath.GetFileName(),
                _ => Task.CompletedTask,
                x => new[] { new StringBuildMessage { Message = x } }),
            $"==== {rustupPath.GetFileName()} done. ====\n",
            $"==== {rustupPath.GetFileName()} cancelled.====\n",
            ct);
    }
}

public struct Toolchain
{
    public string Name { get; set; }

    public string Version { get; set; }

    public bool IsDefault { get; set; }
}
