using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using KS.RustAnalyzer.NodeEnhancements;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.Infrastructure;

public interface ISettingsService
{
    string GetRaw(string type, PathEx fullItemPath);

    Task<string> GetAsync(string type, PathEx fullItemPath);

    Task SetAsync(string type, PathEx fullItemPath, string value);
}

[ExportWorkspaceServiceFactory(WorkspaceServiceFactoryOptions.None, typeof(ISettingsService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SettingsServiceFactory : IWorkspaceServiceFactory
{
    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public ILogger L { get; set; }

    public object CreateService(IWorkspace workspaceContext)
    {
        return new SettingsService(
            (PathEx)workspaceContext.Location,
            workspaceContext.GetSettingsManager(),
            async () => await Options.GetLiveInstanceAsync(),
            new TL { T = T, L = L, });
    }
}

public class SettingsInfo
{
    public string Kind { get; set; }

    public Func<string, string> Getter { get; set; }

    public Func<bool, bool, bool, bool> ShouldDisplay { get; set; }
}

public sealed class SettingsService : ISettingsService
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

    public static readonly IReadOnlyDictionary<string, SettingsInfo> PropertyInfo =
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

    private readonly PathEx _location;
    private readonly IWorkspaceSettingsManager _settingsManager;
    private readonly Func<Task<ISettingsServiceDefaults>> _hostWideOptionsGetter;
    private readonly TL _tl;

    public SettingsService(PathEx location, IWorkspaceSettingsManager settingsManager, Func<Task<ISettingsServiceDefaults>> hostWideOptionsGetter, TL tl)
    {
        _location = location;
        _settingsManager = settingsManager;
        _hostWideOptionsGetter = hostWideOptionsGetter;
        _tl = tl;
    }

    // TODO: 1.5. RELEASE: Unit test this.
    public string GetRaw(string type, PathEx fullItemPath)
    {
        if (_settingsManager == null)
        {
            _tl.T.TrackException(new NullReferenceException("CurrentWorkspace is null."));
            return default;
        }

        var settings = _settingsManager.GetAggregatedSettings(SettingsTypes.Generic);
        var result = settings.GetProperty(CreateKeyName(type, fullItemPath), out string value);
        if (result != WorkspaceSettingsResult.Success || value.IsNullOrEmptyOrWhiteSpace())
        {
            value = string.Empty;
        }

        return value;
    }

    // TODO: 1.5 RELEASE: Unit test this.
    public async Task<string> GetAsync(string type, PathEx fullItemPath)
    {
        var value = GetRaw(type, fullItemPath);
        if (value.IsNullOrEmptyOrWhiteSpace())
        {
            var hostWideOptions = await _hostWideOptionsGetter();
            value = (string)hostWideOptions.GetType().GetProperty(type).GetValue(hostWideOptions, null);
            if (value.IsNullOrEmptyOrWhiteSpace())
            {
                value = string.Empty;
            }
        }

        return PropertyInfo[type].Getter(value);
    }

    public async Task SetAsync(string type, PathEx fullItemPath, string value)
    {
        _tl.T.TrackEvent("SaveSettings", ("Type", type), ("RelativePath", fullItemPath), ("CmdLineArgs", value));
        if (_settingsManager == null)
        {
            _tl.T.TrackException(new NullReferenceException("CurrentWorkspace is null."));
            return;
        }

        try
        {
            using var persistence = await _settingsManager.GetPersistanceAsync(autoCommit: true);

            var writer = await persistence.GetWriter(SettingsTypes.Generic);
            writer.SetProperty(CreateKeyName(type, fullItemPath), value);
        }
        catch (Exception e)
        {
            _tl.T.TrackException(e);
            _tl.L.WriteError("Exception: {0}.", e);
        }
    }

    private string CreateKeyName(string type, PathEx fullItemPath)
    {
        var kind = PropertyInfo[type].Kind;
        var relItemPath = _location.MakeRelativePath(fullItemPath);

        return $"{Vsix.Name}-{kind}-{type}-{relItemPath}";
    }
}
