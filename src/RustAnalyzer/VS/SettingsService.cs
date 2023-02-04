using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using static KS.RustAnalyzer.VS.NodeBrowseObjectProvider;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.VS;

public interface ISettingsService
{
    string Get(string kind, string type, string item);

    Task SetAsync(string kind, string type, string item, string value);
}

[Export(typeof(ISettingsService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SettingsService : ISettingsService
{
    public const string KindDebugger = "Debugger";
    public const string TypeCmdLineArgs = nameof(FileSystemBrowseObject.CommandLineArguments);

    private readonly TL _tl;
    private readonly IVsFolderWorkspaceService _workspaceService;

    [ImportingConstructor]
    public SettingsService([Import] IVsFolderWorkspaceService workspaceService, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };

        _workspaceService = workspaceService;
    }

    public string Get(string kind, string type, string item)
    {
        var settingsManager = _workspaceService.CurrentWorkspace.GetSettingsManager();
        if (settingsManager == null)
        {
            _tl.T.TrackException(new NullReferenceException("CurrentWorkspace is null."));
            return default;
        }

        var settings = settingsManager.GetAggregatedSettings(SettingsTypes.Generic);
        var result = settings.GetProperty(CreateKeyName(kind, type, item), out string cmdLineArgs);
        if (result == WorkspaceSettingsResult.Success)
        {
            return cmdLineArgs;
        }

        return string.Empty;
    }

    public async Task SetAsync(string kind, string type, string item, string value)
    {
        _tl.T.TrackEvent("SaveSettings", ("Type", type), ("RelativePath", item), ("CmdLineArgs", value));
        var settingsManager = _workspaceService.CurrentWorkspace.GetSettingsManager();
        if (settingsManager == null)
        {
            _tl.T.TrackException(new NullReferenceException("CurrentWorkspace is null."));
            return;
        }

        try
        {
            using var persistence = await settingsManager.GetPersistanceAsync(autoCommit: true);

            var writer = await persistence.GetWriter(SettingsTypes.Generic);
            writer.SetProperty(CreateKeyName(kind, type, item), value);
        }
        catch (Exception e)
        {
            _tl.T.TrackException(e);
            _tl.L.WriteError("Exception: {0}.", e);
        }
    }

    private static string CreateKeyName(string kind, string type, string item)
    {
        return $"rust-analyzer.vs-{kind}-{type}-{item}";
    }
}
