using System;
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

    private static PathEx GetRustupPath() => (PathEx)Environment.GetEnvironmentVariable("USERPROFILE") + (PathEx)@".rustup";
}
