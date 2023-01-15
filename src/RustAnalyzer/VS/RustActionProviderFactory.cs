using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KS.RustAnalyzer.Cargo;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Extensions.VS;

namespace KS.RustAnalyzer.VS;

[ExportFileContextActionProvider(
    type: ProviderType,
    supportedContextTypeGuids: new[] { BuildContextTypes.BuildContextType, BuildContextTypes.CleanContextType, })]
public class RustActionProviderFactory : IWorkspaceProviderFactory<IFileContextActionProvider>, IVsCommandActionProvider
{
    public const string ProviderType = "F8C470E5-0000-498C-80B8-DA2674A82B88";

    private readonly RustOutputPane _outputPane;

    private ITelemetryService _telemetryService;

    [ImportingConstructor]
    public RustActionProviderFactory(RustOutputPane outputPane)
    {
        _outputPane = outputPane;
    }

    public IFileContextActionProvider CreateProvider(IWorkspace workspaceContext)
    {
        _telemetryService = workspaceContext.GetService<ITelemetryService>();
        _telemetryService.TrackEvent(
            "Create Context Action Provider",
            new[] { ("Location", workspaceContext.Location) });

        return new FileContextActionProvider(workspaceContext, _outputPane, _telemetryService);
    }

    public IReadOnlyCollection<CommandID> GetSupportedVsCommands()
    {
        return new CommandID[]
        {
            // For additional menu items like restore, clippy, fmt, etc.
        };
    }
}

public class FileContextActionProvider : IFileContextActionProvider
{
    private readonly IWorkspace _workspace;
    private readonly RustOutputPane _outputPane;
    private readonly ITelemetryService _telemetryService;
    private readonly Func<string, Task> _showMessageBox;

    public FileContextActionProvider(IWorkspace workspace, RustOutputPane outputPane, ITelemetryService telemetryService)
    {
        _workspace = workspace;
        _outputPane = outputPane;
        _telemetryService = telemetryService;
        _showMessageBox = ShowMessageBoxAsync;
    }

    public async Task<IReadOnlyList<IFileContextAction>> GetActionsAsync(string filePath, FileContext fileContext, CancellationToken cancellationToken)
    {
        await _workspace.JTF.SwitchToMainThreadAsync();

        _outputPane.InitializeOutputPanes();

        var actions = new List<IFileContextAction>();

        if (RustHelpers.IsCargoFile(filePath))
        {
            actions.Add(new RustBuildFileContextAction(filePath, fileContext, _outputPane, _telemetryService, _showMessageBox));
        }
        else if (RustHelpers.IsRustFile(filePath) && CargoManifest.GetParentCargoManifest(filePath, _workspace.Location, out string parentCargoPath))
        {
            actions.Add(new RustBuildFileContextAction(parentCargoPath, fileContext, _outputPane, _telemetryService, _showMessageBox, fileContextMenuVisible: false));
        }

        return actions;
    }

    private async Task ShowMessageBoxAsync(string message)
    {
        await _workspace.JTF.SwitchToMainThreadAsync();

        MessageBox.Show(message, "rust-analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

public class RustBuildFileContextAction : IFileContextAction, IVsCommandItem
{
    private static readonly IReadOnlyDictionary<Guid, (uint, Func<string, string, RustOutputPane, ITelemetryService, Func<string, Task>, Task<bool>>)> FileContextActionInfo =
        new Dictionary<Guid, (uint, Func<string, string, RustOutputPane, ITelemetryService, Func<string, Task>, Task<bool>>)>
        {
            [BuildContextTypes.BuildContextTypeGuid] = (PredefinedCmdId.CmdIdBuildActionContext, CargoExeRunner.BuildAsync),
            [BuildContextTypes.CleanContextTypeGuid] = (PredefinedCmdId.CmdIdCleanActionContext, CargoExeRunner.CleanAsync),
        };

    private readonly ITelemetryService _telemetryService;
    private readonly Func<string, Task> _showMessageBox;
    private readonly bool _fileContextMenuVisible;

    public RustBuildFileContextAction(string filePath, FileContext source, RustOutputPane outputPane, ITelemetryService telemetryService, Func<string, Task> showMessageBox, bool fileContextMenuVisible = true)
    {
        Source = source;
        FilePath = filePath;
        OutputPane = outputPane;
        _telemetryService = telemetryService;
        _showMessageBox = showMessageBox;
        _fileContextMenuVisible = fileContextMenuVisible;
        (CommandId, CommandFunc) = FileContextActionInfo[source.ContextType];
    }

    public Guid CommandGroup => _fileContextMenuVisible ? PredefinedCmdGuid.GuidWorkspaceExplorerBuildActionCmdSet : Guid.Empty;

    public uint CommandId { get; private set; }

    public Func<string, string, RustOutputPane, ITelemetryService, Func<string, Task>, Task<bool>> CommandFunc { get; private set; }

    public FileContext Source { get; }

    public string FilePath { get; }

    public RustOutputPane OutputPane { get; }

    public string DisplayName => "Open Folder uses the name defined in .vsct file.";

    public async Task<IFileContextActionResult> ExecuteAsync(IProgress<IFileContextActionProgressUpdate> progress, CancellationToken cancellationToken)
    {
        var result = await CommandFunc(FilePath, (Source.Context as BuildConfigurationContext).BuildConfiguration, OutputPane, _telemetryService, _showMessageBox);
        return CreateBuildProjectIncrementalResultFromBoolean(result);
    }

    protected static IFileContextActionResult CreateBuildProjectIncrementalResultFromBoolean(bool buildSucceeded)
    {
        // Assuming there is only project being compiled.
        return new BuildProjectIncrementalResult(
            isSuccess: buildSucceeded,
            succeeded: buildSucceeded ? 1 : 0,
            failed: !buildSucceeded ? 1 : 0,
            upToDate: 0);
    }
}
