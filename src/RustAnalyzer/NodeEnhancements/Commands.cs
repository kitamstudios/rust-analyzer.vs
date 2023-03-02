using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.NodeEnhancements;

public abstract class BaseToolChainCommand<T> : BaseCommand<T>
    where T : class, new()
{
    protected abstract Func<IToolChainService, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>> Operation { get; }

    protected abstract string GetOptions(Options opts);

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var selectedItems = GetSelectedItems();
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

        var selectedPath = GetSelectedItems().FirstOrDefault();

        var mefRepo = await CommunityVS.Services.GetComponentModelAsync();
        string profile = await GetProfileAsync(selectedPath, mefRepo);
        var toolChainSvc = mefRepo.GetService<IToolChainService>();
        var bos = mefRepo.GetService<IBuildOutputSink>();

        var opts = await Options.GetLiveInstanceAsync();
        await Operation(toolChainSvc)(
            new BuildTargetInfo { ManifestPath = selectedPath, AdditionalBuildArgs = GetOptions(opts), Profile = profile, WorkspaceRoot = selectedPath.GetDirectoryName(), },
            new BuildOutputSinks { OutputSink = bos, BuildActionProgressReporter = bm => Task.CompletedTask },
            default);
    }

    protected static IEnumerable<PathEx> GetSelectedItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return VsCommon.GetSelectedItems().Select(si => (PathEx)si.GetFullName());
    }

    private static async Task<string> GetProfileAsync(PathEx selectedPath, IComponentModel2 mefRepo)
    {
        var w = mefRepo.GetService<IVsFolderWorkspaceService>().CurrentWorkspace;
        var projCfgSvc = await w.GetServiceAsync<IProjectConfigurationService>();
        var profile = projCfgSvc.GetActiveProjectBuildConfiguration(new ProjectTargetFileContext(selectedPath));
        return profile;
    }
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.CargoClippy)]
public class CargoClippy : BaseToolChainCommand<CargoClippy>
{
    protected override Func<IToolChainService, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>> Operation => its => its.RunClippyAsync;

    protected override string GetOptions(Options opts) => opts.DefailtCargoClippyArgs;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.CargoFmt)]
public class CargoFmt : BaseToolChainCommand<CargoFmt>
{
    protected override Func<IToolChainService, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>> Operation => its => its.RunFmtAsync;

    protected override string GetOptions(Options opts) => opts.DefailtCargoFmtArgs;
}
