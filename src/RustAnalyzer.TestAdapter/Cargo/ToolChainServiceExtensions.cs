using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
public static class ToolChainServiceExtensions
{
    private const string DefaultTargetTriple = "x86_64-pc-windows-msvc";

    private static readonly Regex NameCracker =
        new (@"^((?<name>.*)(?<default> \(default\))?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

    private static readonly Regex VersionCracker =
        new (@"^rustc (?<version>\d+.\d+.\d+(-.*)?) (\(.* (?<date>\d{4}-\d{2}-\d{2})\))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        return (root + "bin", root + $@"lib\rustlib\{DefaultTargetTriple}\lib");
    }

    public static async Task<Toolchain[]> GetInstalledToolchainsAsync(PathEx workingDirectory, CancellationToken ct)
    {
        var output = await GetCommandOutput("rustup", "show --verbose", workingDirectory, ct);
        var tcRaw = output
            .Select(x => x.Trim())
            .Where(x => !x.IsNullOrEmptyOrWhiteSpace())
            .SkipWhile(x => !x.Equals("installed toolchains"))
            .Skip(2)
            .TakeWhile(x => !x.Equals("active toolchain"))
            .ToList();

        var tcs = Enumerable.Range(0, tcRaw.Count / 2)
            .Select(i => (tcRaw[i * 2], tcRaw[(i * 2) + 1]))
            .Select(x => (NameCracker.Match(x.Item1), VersionCracker.Match(x.Item2)))
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
            .Split(' ')[0];
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
}

public struct Toolchain
{
    public string Name { get; set; }

    public string Version { get; set; }

    public bool IsDefault { get; set; }
}
