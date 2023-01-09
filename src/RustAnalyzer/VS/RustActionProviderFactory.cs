using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Cargo;
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

    [ImportingConstructor]
    public RustActionProviderFactory(RustOutputPane outputPane)
    {
        _outputPane = outputPane;
    }

    public IFileContextActionProvider CreateProvider(IWorkspace workspaceContext)
    {
        return new FileContextActionProvider(workspaceContext, _outputPane);
    }

    public IReadOnlyCollection<CommandID> GetSupportedVsCommands()
    {
        return new[]
        {
            new CommandID(Guids.GuidWorkspaceExplorerBuildActionCmdSet, PkgCmdId.CmdIdBuildActionContext),
        };
    }
}

public class FileContextActionProvider : IFileContextActionProvider
{
    private readonly IWorkspace _workspace;
    private readonly RustOutputPane _outputPane;

    public FileContextActionProvider(IWorkspace workspace, RustOutputPane outputPane)
    {
        _workspace = workspace;
        _outputPane = outputPane;
    }

    public async Task<IReadOnlyList<IFileContextAction>> GetActionsAsync(string filePath, FileContext fileContext, CancellationToken cancellationToken)
    {
        await _workspace.JTF.SwitchToMainThreadAsync();

        _outputPane.InitializeOutputPanes();

        var actions = new List<IFileContextAction>();

        if (RustHelpers.IsCargoFile(filePath))
        {
            actions.Add(new BuildCargoFileContextAction(filePath, fileContext, _outputPane));
        }
        else if (RustHelpers.IsRustFile(filePath))
        {
            var cargo = await _workspace.GetParentCargoManifestAsync(filePath);

            if (cargo != null)
            {
                actions.Add(new BuildRustFileContextAction(filePath, fileContext, _outputPane));
            }
        }

        return actions;
    }
}

public sealed class BuildRustFileContextAction : BuildFileContextAction, IFileContextAction, IVsCommandItem
{
    public BuildRustFileContextAction(string filePath, FileContext fileContext, RustOutputPane outputPane)
        : base(filePath, fileContext, outputPane)
    {
    }

    public override async Task<IFileContextActionResult> ExecuteAsync(IProgress<IFileContextActionProgressUpdate> progress, CancellationToken cancellationToken)
    {
        var result = await CargoExeRunner.CompileFileAsync(FilePath, OutputPane);
        return CreateBuildProjectIncrementalResultFromBoolean(result);
    }
}

public sealed class BuildCargoFileContextAction : BuildFileContextAction, IFileContextAction, IVsCommandItem
{
    public BuildCargoFileContextAction(string filePath, FileContext fileContext, RustOutputPane outputPane)
        : base(filePath, fileContext, outputPane)
    {
    }

    public override async Task<IFileContextActionResult> ExecuteAsync(IProgress<IFileContextActionProgressUpdate> progress, CancellationToken cancellationToken)
    {
        var result = await CargoExeRunner.CompileProjectAsync(FilePath, (Source.Context as RustBuildContext).BuildConfiguration, OutputPane);
        return CreateBuildProjectIncrementalResultFromBoolean(result);
    }
}

public abstract class BuildFileContextAction
{
    public BuildFileContextAction(string filePath, FileContext fileContext, RustOutputPane outputPane)
    {
        Source = fileContext;
        FilePath = filePath;
        OutputPane = outputPane;
    }

    public Guid CommandGroup => Guids.GuidWorkspaceExplorerBuildActionCmdSet;

    public uint CommandId => PkgCmdId.CmdIdBuildActionContext;

    public FileContext Source { get; }

    public string FilePath { get; }

    public RustOutputPane OutputPane { get; }

    public string DisplayName => "Open Folder uses the name defined in .vsct file.";

    public abstract Task<IFileContextActionResult> ExecuteAsync(IProgress<IFileContextActionProgressUpdate> progress, CancellationToken cancellationToken);

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
