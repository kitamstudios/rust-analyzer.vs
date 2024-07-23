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

public static class ToolChainServiceExtensions
{
    private static readonly Regex DefaultToolChainCracker = new (@"default_toolchain\s*=\s*\""(.*)\""");

    private static readonly IReadOnlyDictionary<string, string> OpNameToToolNameMapper = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "cargo", "cargo" },
        { "build", "rustc" },
        { "clean", "rustc" },
        { "test", "rustc" },
        { "fmt", "cargo-fmt" },
        { "clippy", "cargo-clippy" },
    };

    public static string GetActiveToolChain()
    {
        var rustupSettingsPath = GetRustupPath() + (PathEx)@"settings.toml";
        if (rustupSettingsPath.FileExists())
        {
            var dtcLine = File.ReadLines(rustupSettingsPath).Where(l => DefaultToolChainCracker.IsMatch(l)).FirstOrDefault();
            if (dtcLine != null)
            {
                var m = DefaultToolChainCracker.Match(dtcLine);
                if (m.Success)
                {
                    return m.Groups[1].Value;
                }
            }
        }

        return "nightly-x86_64-pc-windows-msvc";
    }

    public static PathEx GetLibPath()
    {
        return GetRustupPath() + (PathEx)@$"toolchains\{GetActiveToolChain()}\lib\rustlib\x86_64-pc-windows-msvc\lib";
    }

    public static PathEx GetBinPath()
    {
        return GetRustupPath() + (PathEx)@$"toolchains\{GetActiveToolChain()}\bin";
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

    public static async Task<string> GetCommandOutput(string opName, string versionArgs, PathEx workingDirectory, CancellationToken ct)
    {
        var toolName = OpNameToToolNameMapper[opName];
        using var proc = ProcessRunner.Run("cmd.exe", new[] { "/c", $"{toolName} {versionArgs}" }, workingDirectory, ImmutableDictionary<string, string>.Empty, ct);

        var ec = await proc;
        var output = proc.StandardOutputLines.Concat(proc.StandardErrorLines).ToArray();
        if (ec != 0)
        {
            return $"{toolName} returned {ec}.\nOutput: {output}";
        }

        return string.Join(string.Empty, output.Where(l => !l.IsNullOrEmptyOrWhiteSpace()));
    }

    private static PathEx GetRustupPath() => (PathEx)Environment.GetEnvironmentVariable("USERPROFILE") + (PathEx)@".rustup";
}
