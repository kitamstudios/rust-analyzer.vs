using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;

namespace KS.RustAnalyzer.Shell;

using ToolchainOperation = Func<IToolChainService, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>>;

public abstract class BaseToolChainCommand<T> : BaseCommand<T>
    where T : class, new()
{
    protected BaseToolChainCommand()
    {
        CmdServices = new CmdServices(() => Package);
    }

    public CmdServices CmdServices { get; }

    protected abstract ToolchainOperation Operation { get; }

    protected abstract string GetOptions(Options opts);

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var selectedItems = CmdServices.GetSelectedItems();
        if (selectedItems.Count() != 1)
        {
            Command.Visible = Command.Enabled = false;
            return;
        }

        var path = selectedItems.First();
        Command.Visible = Command.Enabled = path.IsManifest() && path.FileExists();
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var selectedPath = CmdServices.GetSelectedItems().FirstOrDefault();
        await CmdServices.ExecuteToolchainOperationAsync(Operation, selectedPath, GetOptions);
    }
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdCargoClippy)]
public class CargoClippyCommand : BaseToolChainCommand<CargoClippyCommand>
{
    protected override ToolchainOperation Operation => its => its.RunClippyAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoClippyArgs;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdCargoFmt)]
public class CargoFmtCommand : BaseToolChainCommand<CargoFmtCommand>
{
    protected override ToolchainOperation Operation => its => its.RunFmtAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoFmtArgs;
}

public abstract class BaseBuildToolChainCommand<T> : BaseCommand<T>
    where T : class, new()
{
    protected BaseBuildToolChainCommand()
    {
        CmdServices = new CmdServices(() => Package);
    }

    public CmdServices CmdServices { get; }

    protected abstract ToolchainOperation Operation { get; }

    protected abstract string GetOptions(Options opts);

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Command.Visible = Command.Enabled = Command.Supported = IsCommandActive();
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var selectedPath = GetManifestPath();
        await CmdServices.ExecuteToolchainOperationAsync(Operation, selectedPath, GetOptions);
    }

    private PathEx GetManifestPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return CmdServices.GetWorkspaceRoot() + Constants.ManifestFileName2;
    }

    private bool IsCommandActive()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var workspaceRoot = CmdServices.GetWorkspaceRoot();
        return (workspaceRoot + Constants.ManifestFileName2).FileExists() && CmdServices.IsIdeInDesignMode();
    }
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdBuildAll)]
public class BuildAllCommand : BaseBuildToolChainCommand<BuildAllCommand>
{
    protected override ToolchainOperation Operation => its => its.BuildAsync;

    protected override string GetOptions(Options opts) => opts.AdditionalBuildArguments;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdCleanAll)]
public class CleanAllCommand : BaseBuildToolChainCommand<CleanAllCommand>
{
    protected override ToolchainOperation Operation => its => its.CleanAsync;

    protected override string GetOptions(Options opts) => string.Empty;
}
