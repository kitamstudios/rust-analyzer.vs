using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using ShellInterop = Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Shell;

using ToolchainOperation = System.Func<KS.RustAnalyzer.TestAdapter.Common.IToolChainService, System.Func<KS.RustAnalyzer.TestAdapter.Common.BuildTargetInfo, KS.RustAnalyzer.TestAdapter.Common.BuildOutputSinks, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>>>;

public sealed class CmdServices
{
    private IComponentModel2 _mef;
    private ILogger _l;
    private ITelemetryService _t;
    private ShellInterop.IVsSolution _solution;
    private ShellInterop.IVsDebugger _debugger;
    private IToolChainService _toolChainService;
    private IBuildOutputSink _buildOutputSink;

    public CmdServices(Func<AsyncPackage> getPackage)
    {
        GetPackage = getPackage;
    }

    public Func<AsyncPackage> GetPackage { get; }

    private IComponentModel2 Mef => _mef ??= GetPackage().GetService<SComponentModel, IComponentModel2>(false);

    private ITelemetryService T => _t ??= Mef?.GetService<ITelemetryService>();

    private ILogger L => _l ??= Mef?.GetService<ILogger>();

    private ShellInterop.IVsSolution Solution => _solution ??= GetPackage().GetService<ShellInterop.SVsSolution, ShellInterop.IVsSolution>(false);

    private ShellInterop.IVsDebugger Debugger => _debugger ??= GetPackage().GetService<ShellInterop.SVsShellDebugger, ShellInterop.IVsDebugger>(false);

    private IToolChainService ToolChainService => _toolChainService ??= Mef?.GetService<IToolChainService>();

    private IBuildOutputSink BuildOutputSink => _buildOutputSink ??= Mef?.GetService<IBuildOutputSink>();

    public async Task ExecuteToolchainOperationAsync(ToolchainOperation op, PathEx manifestPath, Func<Options, string> getOpts)
    {
        var profile = Mef.GetProfile(manifestPath);
        var opts = await Options.GetLiveInstanceAsync();
        await op(ToolChainService)(
            new BuildTargetInfo
            {
                ManifestPath = manifestPath,
                AdditionalBuildArgs = getOpts(opts),
                Profile = profile,
                WorkspaceRoot = manifestPath.GetDirectoryName(),
            },
            new BuildOutputSinks { OutputSink = BuildOutputSink, BuildActionProgressReporter = bm => Task.CompletedTask },
            default);
    }

    public IEnumerable<PathEx> GetSelectedItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return VsCommon.GetSelectedItems()
            .Select(si => (PathEx?)si.GetFullName())
            .Where(p => p.HasValue).Select(p => p.Value);
    }

    public PathEx GetWorkspaceRoot()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string workspaceRoot = null;
        if (ErrorHandler.Failed(Solution?.GetSolutionInfo(out workspaceRoot, out var _, out var _) ?? VSConstants.E_FAIL))
        {
            L.WriteError("Unable to determine workspace root.");
        }

        return (PathEx)workspaceRoot;
    }

    public bool IsIdeInDesignMode()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dbgMode = new ShellInterop.DBGMODE[1];
        if (ErrorHandler.Failed(Debugger?.GetMode(dbgMode) ?? VSConstants.E_FAIL))
        {
            L.WriteError("Unable to determine debugger mode.");
            return false;
        }

        return dbgMode[0] == ShellInterop.DBGMODE.DBGMODE_Design;
    }
}
