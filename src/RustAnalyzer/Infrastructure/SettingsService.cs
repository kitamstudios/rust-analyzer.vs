using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;
using static KS.RustAnalyzer.NodeEnhancements.NodeBrowseObjectProvider;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.Infrastructure;

public interface ISettingsService
{
    string Get(string type, PathEx fullItemPath);

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
        return new SettingsService((PathEx)workspaceContext.Location, workspaceContext.GetSettingsManager(), new TL { T = T, L = L, });
    }
}

public sealed class SettingsService : ISettingsService
{
    public const string KindDebugger = "Debugger";
    public const string KindBuild = "Build";
    public const string TypeCommandLineArguments = nameof(BrowseObject.CommandLineArguments);
    public const string TypeDebuggerEnvironment = nameof(BrowseObject.DebuggerEnvironment);
    public const string TypeAdditionalBuildArgs = nameof(BrowseObject.AdditionalBuildArguments);
    private readonly PathEx _location;
    private readonly IWorkspaceSettingsManager _settingsManager;
    private readonly TL _tl;

    [ImportingConstructor]
    public SettingsService(PathEx location, IWorkspaceSettingsManager settingsManager, TL tl)
    {
        _location = location;
        _settingsManager = settingsManager;
        _tl = tl;
    }

    public string Get(string type, PathEx fullItemPath)
    {
        if (_settingsManager == null)
        {
            _tl.T.TrackException(new NullReferenceException("CurrentWorkspace is null."));
            return default;
        }

        var settings = _settingsManager.GetAggregatedSettings(SettingsTypes.Generic);
        var result = settings.GetProperty(CreateKeyName(type, fullItemPath), out string value);
        if (result == WorkspaceSettingsResult.Success)
        {
            return value;
        }

        return string.Empty;
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
        string kind;
        if (type == TypeCommandLineArguments)
        {
            kind = KindDebugger;
        }
        else if (type == TypeDebuggerEnvironment)
        {
            kind = KindDebugger;
        }
        else if (type == TypeAdditionalBuildArgs)
        {
            kind = KindBuild;
        }
        else
        {
            throw new NotImplementedException(type);
        }

        var relItemPath = _location.MakeRelativePath(fullItemPath);

        return $"rust-analyzer.vs-{kind}-{type}-{relItemPath}";
    }
}
