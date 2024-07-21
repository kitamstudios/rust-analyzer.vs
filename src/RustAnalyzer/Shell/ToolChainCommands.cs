using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.Shell;

public abstract class BaseToolChainCommand<T> : BaseRustAnalyzerCommand<T>
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
        var profile = mefRepo.GetProfile(selectedPath);
        var toolChainSvc = mefRepo.GetService<IToolChainService>();
        var bos = mefRepo.GetService<IBuildOutputSink>();

        var opts = await Options.GetLiveInstanceAsync();
        await Operation(toolChainSvc)(
            new BuildTargetInfo
            {
                ManifestPath = selectedPath,
                AdditionalBuildArgs = GetOptions(opts),
                Profile = profile,
                WorkspaceRoot = selectedPath.GetDirectoryName(),
            },
            new BuildOutputSinks { OutputSink = bos, BuildActionProgressReporter = bm => Task.CompletedTask },
            default);
    }

    protected static IEnumerable<PathEx> GetSelectedItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return VsCommon.GetSelectedItems().Select(si => (PathEx?)si.GetFullName()).Where(p => p.HasValue).Select(p => p.Value);
    }
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.CargoClippy)]
public class CargoClippy : BaseToolChainCommand<CargoClippy>
{
    protected override Func<IToolChainService, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>> Operation => its => its.RunClippyAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoClippyArgs;
}

[Command(PackageGuids.guidRustAnalyzerPackageString, PackageIds.CargoFmt)]
public class CargoFmt : BaseToolChainCommand<CargoFmt>
{
    protected override Func<IToolChainService, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>> Operation => its => its.RunFmtAsync;

    protected override string GetOptions(Options opts) => opts.DefaultCargoFmtArgs;
}
