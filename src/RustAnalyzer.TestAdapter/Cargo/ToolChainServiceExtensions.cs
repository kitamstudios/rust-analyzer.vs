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

    public static async Task<(string Name, string Version, bool Default)[]> GetInstalledToolchainsAsync(PathEx workingDirectory, CancellationToken ct)
    {
        var output = await GetCommandOutput("rustup", "show --verbose", workingDirectory, ct);
        var nvs = output
            .Select(x => x.Trim())
            .Where(x => !x.IsNullOrEmptyOrWhiteSpace())
            .SkipWhile(x => !x.Equals("installed toolchains"))
            .Skip(2)
            .TakeWhile(x => !x.Equals("active toolchain"))
            .ToList();

        var nvEntries = Enumerable.Range(0, nvs.Count / 2)
            .Select(i => (nvs[i * 2], nvs[(i * 2) + 1]))
            .Select(x => (NameCracker.Match(x.Item1), VersionCracker.Match(x.Item2)))
            .Where(x => x.Item1.Success && x.Item2.Success)
            .Select(x => (
                x.Item1.Groups["name"].Value,
                $"{x.Item2.Groups["version"].Value} ({x.Item2.Groups["version"].Value})",
                x.Item1.Groups["default"].Value.IsNotNullOrEmpty()));

        return nvEntries.ToArray();
    }

    public static async Task<string> GetDefaultToolchainAsync(PathEx workingDirectory, CancellationToken ct)
    {
        return (await GetInstalledToolchainsAsync(workingDirectory, ct)).Where(x => x.Default).First().Name;
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

    public static async Task<string[]> GetCommandOutput(string opName, string versionArgs, PathEx workingDirectory, CancellationToken ct)
    {
        var toolName = OpNameToToolNameMapper[opName];
        using var proc = ProcessRunner.Run("cmd.exe", new[] { "/c", $"{toolName} {versionArgs}" }, workingDirectory, ImmutableDictionary<string, string>.Empty, ct);

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
