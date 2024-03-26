using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Moq;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.Tests.Common;

public static class TestHelpers
{
    public static readonly PathEx ThisTestRoot =
        (PathEx)Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    public static readonly TL TL =
        new ()
        {
            L = Mock.Of<ILogger>(),
            T = Mock.Of<ITelemetryService>(),
        };

    private static readonly ConcurrentDictionary<PathEx, IMetadataService> MetadataServices = new ConcurrentDictionary<PathEx, IMetadataService>();

    public static PathEx RemoveMachineSpecificPaths(this PathEx @this)
        => (PathEx)((string)@this).ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>");

    public static IMetadataService MS(this PathEx @this)
    {
        // NOTE: This simulates the case when a folder with multiple workspaces is opened.
        var root = @this.GetDirectoryName();
        return MetadataServices.GetOrAdd(root, (wr) => new MetadataService(new ToolChainService(TL.T, TL.L), wr, TL));
    }

    public static string Replace(this string str, string old, string @new, StringComparison comparison)
    {
        @new = @new ?? string.Empty;
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(old) || old.Equals(@new, comparison))
        {
            return str;
        }

        int foundAt = 0;
        while ((foundAt = str.IndexOf(old, foundAt, comparison)) != -1)
        {
            str = str.Remove(foundAt, old.Length).Insert(foundAt, @new);
            foundAt += @new.Length;
        }

        return str;
    }

    public static Task<bool> DoBuildAsync(
        this IToolChainService @this,
        PathEx workspacePath,
        PathEx manifestPath,
        string profile,
        string additionalBuildArgs = "",
        string additionalTestDiscoveryArguments = "",
        string additionalTestExecutionArguments = "",
        string testExecutionEnvironment = "")
    {
        return @this.BuildAsync(
                    new BuildTargetInfo
                    {
                        WorkspaceRoot = workspacePath,
                        ManifestPath = manifestPath,
                        Profile = profile,
                        AdditionalBuildArgs = additionalBuildArgs,
                        AdditionalTestDiscoveryArguments = additionalTestDiscoveryArguments,
                        AdditionalTestExecutionArguments = additionalTestExecutionArguments,
                        TestExecutionEnvironment = testExecutionEnvironment,
                    },
                    new BuildOutputSinks { OutputSink = Mock.Of<IBuildOutputSink>(), BuildActionProgressReporter = bm => Task.CompletedTask },
                    default);
    }

    public static string SerializeAndNormalizeObject(this object @this)
    {
        return @this
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(((string)ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase)
            .RegexReplace(@"    ""(Start|End)Time"": ""(.*)"",", string.Empty)
            .RegexReplace(@"    ""Duration"": ""00:00:00.[0-2]\d{6}"",", @"    ""Duration"": ""00:00:00.2000000""")
            .RegexReplace(@"\-[\da-f]{16}\.", "-*.", RegexOptions.IgnoreCase)
            .Replace("note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace\\n", string.Empty);
    }

    public static (PathEx WorkspacePath, PathEx ManifestPath, PathEx TargetPath) GetTestPaths(this string @this, string profile)
    {
        var workspacePath = ThisTestRoot + (PathEx)@this;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath(profile);

        return (WorkspacePath: workspacePath, ManifestPath: manifestPath, TargetPath: targetPath);
    }
}
