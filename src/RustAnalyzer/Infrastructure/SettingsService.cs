using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
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

public sealed class SettingsService : ISettingsService
{
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

        return SettingsInfo.Store[type].Getter(value);
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
        var kind = SettingsInfo.Store[type].Kind;
        var relItemPath = _location.MakeRelativePath(fullItemPath);

        return $"{Vsix.Name}-{kind}-{type}-{relItemPath}";
    }
}
