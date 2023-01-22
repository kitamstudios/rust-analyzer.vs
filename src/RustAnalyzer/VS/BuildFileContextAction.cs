using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Cargo;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Extensions.VS;

namespace KS.RustAnalyzer.VS;

public sealed class BuildFileContextAction : IFileContextAction, IVsCommandItem
{
    private static readonly IReadOnlyDictionary<Guid, (uint, Func<string, string, IOutputWindowPane, ITelemetryService, Func<string, Task>, ILogger, Task<bool>>)> FileContextActionInfo =
        new Dictionary<Guid, (uint, Func<string, string, IOutputWindowPane, ITelemetryService, Func<string, Task>, ILogger, Task<bool>>)>
        {
            [BuildContextTypes.BuildContextTypeGuid] = (PredefinedCmdId.CmdIdBuildActionContext, ExeRunner.BuildAsync),
            [BuildContextTypes.CleanContextTypeGuid] = (PredefinedCmdId.CmdIdCleanActionContext, ExeRunner.CleanAsync),
        };

    private readonly ITelemetryService _telemetryService;
    private readonly Func<string, Task> _showMessageBox;
    private readonly ILogger _logger;
    private readonly bool _fileContextMenuVisible;

    public BuildFileContextAction(string filePath, FileContext source, IOutputWindowPane outputPane, ITelemetryService telemetryService, Func<string, Task> showMessageBox, ILogger logger, bool fileContextMenuVisible = true)
    {
        Source = source;
        FilePath = filePath;
        OutputPane = outputPane;
        _telemetryService = telemetryService;
        _showMessageBox = showMessageBox;
        _logger = logger;
        _fileContextMenuVisible = fileContextMenuVisible;
        (CommandId, CommandFunc) = FileContextActionInfo[source.ContextType];
    }

    public Guid CommandGroup => _fileContextMenuVisible ? PredefinedCmdGuid.GuidWorkspaceExplorerBuildActionCmdSet : Guid.Empty;

    public uint CommandId { get; private set; }

    public Func<string, string, IOutputWindowPane, ITelemetryService, Func<string, Task>, ILogger, Task<bool>> CommandFunc { get; private set; }

    public FileContext Source { get; }

    public string FilePath { get; }

    public IOutputWindowPane OutputPane { get; }

    public string DisplayName => "Open Folder uses the name defined in .vsct file.";

    public async Task<IFileContextActionResult> ExecuteAsync(IProgress<IFileContextActionProgressUpdate> progress, CancellationToken cancellationToken)
    {
        var result = await CommandFunc(FilePath, (Source.Context as BuildConfigurationContext).BuildConfiguration, OutputPane, _telemetryService, _showMessageBox, _logger);
        return CreateBuildProjectIncrementalResultFromBoolean(result);
    }

    private static IFileContextActionResult CreateBuildProjectIncrementalResultFromBoolean(bool buildSucceeded)
    {
        // Assuming there is only project being compiled.
        return new BuildProjectIncrementalResult(
            isSuccess: buildSucceeded,
            succeeded: buildSucceeded ? 1 : 0,
            failed: !buildSucceeded ? 1 : 0,
            upToDate: 0);
    }
}
