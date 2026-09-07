using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

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
    public LegacyLogger L { get; set; }

    [Import]
    public ILoggerFactory LoggerFactory { get; set; }

    public object CreateService(IWorkspace workspaceContext)
    {
        var logger = LoggerFactory?.CreateLogger(
            typeof(SettingsService).FullName)
            ?? LegacyLoggerBridge.ToMelLogger(L);
        return new SettingsService(
            (PathEx)workspaceContext.Location,
            workspaceContext.GetSettingsManager(),
            async () => await Options.GetLiveInstanceAsync(),
            logger);
    }
}

public sealed class SettingsService : ISettingsService
{
    private readonly PathEx _location;
    private readonly IWorkspaceSettingsManager _settingsManager;
    private readonly Func<Task<ISettingsServiceDefaults>> _hostWideOptionsGetter;
    private readonly MelLogger _logger;

    public SettingsService(PathEx location, IWorkspaceSettingsManager settingsManager, Func<Task<ISettingsServiceDefaults>> hostWideOptionsGetter, TL tl)
        : this(
            location,
            settingsManager,
            hostWideOptionsGetter,
            LegacyLoggerBridge.ToMelLogger(tl.L))
    {
    }

    public SettingsService(
        PathEx location,
        IWorkspaceSettingsManager settingsManager,
        Func<Task<ISettingsServiceDefaults>> hostWideOptionsGetter,
        MelLogger logger)
    {
        _location = location;
        _settingsManager = settingsManager;
        _hostWideOptionsGetter = hostWideOptionsGetter;
        _logger = logger;
    }

    public string GetRaw(string type, PathEx fullItemPath)
    {
        if (_settingsManager == null)
        {
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
        if (_settingsManager == null)
        {
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
            _logger.LogError(
                new EventId(1, "SettingsPersistenceFailed"),
                e,
                "Exception.");
        }
    }

    private string CreateKeyName(string type, PathEx fullItemPath)
    {
        var kind = SettingsInfo.Store[type].Kind;
        var relItemPath = _location.MakeRelativePath(fullItemPath);

        return $"{Vsix.Name}-{kind}-{type}-{relItemPath}";
    }
}
