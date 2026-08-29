using System;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;

namespace KS.RustAnalyzer.Shell;

using ToolchainOperation = System.Func<KS.RustAnalyzer.TestAdapter.Common.IToolchainService, System.Func<KS.RustAnalyzer.TestAdapter.Common.BuildTargetInfo, KS.RustAnalyzer.TestAdapter.Common.BuildOutputSinks, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>>>;

public abstract class BaseToolchainCommand<T> : BaseCommand<T>
    where T : class, new()
{
    private readonly PrerequisiteProcessState _prerequisiteState;

    protected BaseToolchainCommand()
        : this(PrerequisiteProcessState.Current)
    {
    }

    protected BaseToolchainCommand(PrerequisiteProcessState prerequisiteState)
    {
        _prerequisiteState = EnsureArg.IsNotNull(
            prerequisiteState,
            nameof(prerequisiteState),
            options => options.WithException(
                new ArgumentNullException(nameof(prerequisiteState))));
        CmdServices = new CmdServices(() => Package);
    }

    public CmdServices CmdServices { get; }

    protected abstract ToolchainOperation Operation { get; }

    protected abstract string GetOptions(Options opts);

    protected sealed override void BeforeQueryStatus(EventArgs e)
    {
        if (!_prerequisiteState.IsAvailable)
        {
            Command.Visible = Command.Enabled = Command.Supported = false;
            return;
        }

#pragma warning disable VSTHRD010
        Command.Supported = true;
        BeforeQueryStatusReady(e);
#pragma warning restore VSTHRD010
    }

    protected virtual void BeforeQueryStatusReady(EventArgs e)
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

    protected sealed override Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        return !_prerequisiteState.IsAvailable
            ? Task.CompletedTask
            : ExecuteReadyAsync(e);
    }

    protected virtual async Task ExecuteReadyAsync(OleMenuCmdEventArgs e)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();

        if (!_prerequisiteState.IsAvailable)
        {
            return;
        }

        var selectedPath = CmdServices.GetSelectedItems().FirstOrDefault();
        await CmdServices.ExecuteToolchainOperationAsync(Operation, selectedPath, GetOptions);
    }
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdCargoClippy)]
public class CargoClippyCommand : BaseToolchainCommand<CargoClippyCommand>
{
    protected override ToolchainOperation Operation => its => its.RunClippyAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoClippyArgs;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdCargoFmt)]
public class CargoFmtCommand : BaseToolchainCommand<CargoFmtCommand>
{
    protected override ToolchainOperation Operation => its => its.RunFmtAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoFmtArgs;
}

public abstract class BaseBuildToolChainCommand<T> : BaseCommand<T>
    where T : class, new()
{
    private readonly PrerequisiteProcessState _prerequisiteState;

    protected BaseBuildToolChainCommand()
        : this(PrerequisiteProcessState.Current)
    {
    }

    protected BaseBuildToolChainCommand(PrerequisiteProcessState prerequisiteState)
    {
        _prerequisiteState = EnsureArg.IsNotNull(
            prerequisiteState,
            nameof(prerequisiteState),
            options => options.WithException(
                new ArgumentNullException(nameof(prerequisiteState))));
        CmdServices = new CmdServices(() => Package);
    }

    public CmdServices CmdServices { get; }

    protected abstract ToolchainOperation Operation { get; }

    protected abstract string GetOptions(Options opts);

    protected sealed override void BeforeQueryStatus(EventArgs e)
    {
        if (!_prerequisiteState.IsAvailable)
        {
            Command.Visible = Command.Enabled = Command.Supported = false;
            return;
        }

#pragma warning disable VSTHRD010
        BeforeQueryStatusReady(e);
#pragma warning restore VSTHRD010
    }

    protected virtual void BeforeQueryStatusReady(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Command.Visible = Command.Enabled = Command.Supported = IsCommandActive();
    }

    protected sealed override Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        return !_prerequisiteState.IsAvailable
            ? Task.CompletedTask
            : ExecuteReadyAsync(e);
    }

    protected virtual async Task ExecuteReadyAsync(OleMenuCmdEventArgs e)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();

        if (!_prerequisiteState.IsAvailable)
        {
            return;
        }

        var selectedPath = GetManifestPath();
        await CmdServices.ExecuteToolchainOperationAsync(Operation, selectedPath, GetOptions);
    }

    protected PathEx GetManifestPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return CmdServices.GetWorkspaceRoot() + Constants.ManifestFileName2;
    }

    protected string GetToolArgsFromSettings(string argName)
        => RustAnalyzerPackage.JTF.Run(
            async () =>
            {
                await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();

                return await CmdServices.SettingsService.GetAsync(argName, GetManifestPath());
            });

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

    protected override string GetOptions(Options opts) => GetToolArgsFromSettings(SettingsInfo.TypeAdditionalBuildArguments);
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdCleanAll)]
public class CleanAllCommand : BaseBuildToolChainCommand<CleanAllCommand>
{
    protected override ToolchainOperation Operation => its => its.CleanAsync;

    protected override string GetOptions(Options opts) => string.Empty;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdClippyAll)]
public class ClippyAll : BaseBuildToolChainCommand<ClippyAll>
{
    protected override ToolchainOperation Operation => its => its.RunClippyAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoClippyArgs;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.IdFmtAll)]
public class FmtAllCommand : BaseBuildToolChainCommand<FmtAllCommand>
{
    protected override ToolchainOperation Operation => its => its.RunFmtAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoFmtArgs;
}
