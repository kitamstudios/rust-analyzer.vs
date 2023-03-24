using System;
using System.Collections.Generic;
using KS.RustAnalyzer.NodeEnhancements;

namespace KS.RustAnalyzer.Infrastructure;

public class SettingsInfo
{
    public const string KindDebugger = "Debugger";
    public const string KindBuild = "Build";
    public const string KindTest = "Test";
    public const string TypeCommandLineArguments = nameof(NodeBrowseObject.CommandLineArguments);
    public const string TypeDebuggerEnvironment = nameof(NodeBrowseObject.DebuggerEnvironment);
    public const string TypeAdditionalBuildArguments = nameof(NodeBrowseObject.AdditionalBuildArguments);
    public const string TypeAdditionalTestDiscoveryArguments = nameof(NodeBrowseObject.AdditionalTestDiscoveryArguments);
    public const string TypeAdditionalTestExecutionArguments = nameof(NodeBrowseObject.AdditionalTestExecutionArguments);
    public const string TypeTestExecutionEnvironment = nameof(NodeBrowseObject.TestExecutionEnvironment);

    public static readonly IReadOnlyDictionary<string, SettingsInfo> Store =
        new Dictionary<string, SettingsInfo>
        {
            [TypeCommandLineArguments] =
                new SettingsInfo
                {
                    Kind = KindDebugger,
                    Getter = x => x,
                    ShouldDisplay = (hasTargets, isExe, isManifest) => isExe,
                },
            [TypeDebuggerEnvironment] =
                new SettingsInfo
                {
                    Kind = KindDebugger,
                    Getter = EnvironmentExtensions.GetEnvironmentBlock,
                    ShouldDisplay = (hasTargets, isExe, isManifest) => isExe,
                },
            [TypeAdditionalBuildArguments] =
                new SettingsInfo
                {
                    Kind = KindBuild,
                    Getter = x => x,
                    ShouldDisplay = (hasTargets, isExe, isManifest) => isExe,
                },
            [TypeAdditionalTestDiscoveryArguments] =
                new SettingsInfo
                {
                    Kind = KindTest,
                    Getter = x => x,
                    ShouldDisplay = (hasTargets, isExe, isManifest) => isManifest && hasTargets,
                },
            [TypeAdditionalTestExecutionArguments] =
                new SettingsInfo
                {
                    Kind = KindTest,
                    Getter = x => x,
                    ShouldDisplay = (hasTargets, isExe, isManifest) => isManifest && hasTargets,
                },
            [TypeTestExecutionEnvironment] =
                new SettingsInfo
                {
                    Kind = KindTest,
                    Getter = EnvironmentExtensions.GetEnvironmentBlock,
                    ShouldDisplay = (hasTargets, isExe, isManifest) => isManifest && hasTargets,
                },
        };

    public string Kind { get; set; }

    public Func<string, string> Getter { get; set; }

    public Func<bool, bool, bool, bool> ShouldDisplay { get; set; }
}
