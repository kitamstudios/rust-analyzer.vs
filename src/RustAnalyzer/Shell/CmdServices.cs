using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using ShellInterop = Microsoft.VisualStudio.Shell.Interop;
using WorkspaceBuildMessage = Microsoft.VisualStudio.Workspace.Build.BuildMessage;

namespace KS.RustAnalyzer.Shell;

using ToolchainOperation = System.Func<KS.RustAnalyzer.TestAdapter.Common.IToolchainService, System.Func<KS.RustAnalyzer.TestAdapter.Common.BuildTargetInfo, KS.RustAnalyzer.TestAdapter.Common.BuildOutputSinks, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>>>;

public sealed class CmdServices
{
    private IComponentModel2 _mef;
    private ILogger _l;
    private ShellInterop.IVsSolution _solution;
    private ShellInterop.IVsDebugger _debugger;
    private ShellInterop.IVsUIShell _vsUIShell;
    private IToolchainService _toolchainService;
    private ISettingsService _settingsService;
    private IBuildOutputSink _buildOutputSink;
    private IVsFolderWorkspaceService _folderWorkspaceService;

    public CmdServices(Func<AsyncPackage> getPackage)
    {
        GetPackage = getPackage;
    }

    public Func<AsyncPackage> GetPackage { get; }

    public ShellInterop.IVsUIShell VsUIShell => _vsUIShell ??= GetPackage().GetService<ShellInterop.SVsUIShell, ShellInterop.IVsUIShell>(false);

    public IBuildOutputSink BuildOutputSink => _buildOutputSink ??= Mef?.GetService<IBuildOutputSink>();

    public IComponentModel2 Mef => _mef ??= GetPackage().GetService<SComponentModel, IComponentModel2>(false);

    public ILogger L => _l ??= Mef?.GetService<ILogger>();

    public ShellInterop.IVsSolution Solution => _solution ??= GetPackage().GetService<ShellInterop.SVsSolution, ShellInterop.IVsSolution>(false);

    public ShellInterop.IVsDebugger Debugger => _debugger ??= GetPackage().GetService<ShellInterop.SVsShellDebugger, ShellInterop.IVsDebugger>(false);

    public IToolchainService ToolchainService => _toolchainService ??= Mef?.GetService<IToolchainService>();

    public ISettingsService SettingsService => _settingsService ??= FolderWorkspaceService?.CurrentWorkspace?.GetService<ISettingsService>();

    public IVsFolderWorkspaceService FolderWorkspaceService => _folderWorkspaceService ??= Mef?.GetService<IVsFolderWorkspaceService>();

    private readonly IMapper _buildMessageMapper = new MapperConfiguration(cfg => cfg.CreateMap<DetailedBuildMessage, WorkspaceBuildMessage>()).CreateMapper();

    public async Task ExecuteToolchainOperationAsync(ToolchainOperation op, PathEx manifestPath, Func<Options, string> getOpts)
    {
        var profile = Mef.GetProfile(manifestPath);
        var opts = await Options.GetLiveInstanceAsync();

        var bms = await FolderWorkspaceService.CurrentWorkspace.GetBuildMessageServiceAsync();
        await op(ToolchainService)(
            new BuildTargetInfo
            {
                ManifestPath = manifestPath,
                AdditionalBuildArgs = getOpts(opts),
                Profile = profile,
                WorkspaceRoot = manifestPath.GetDirectoryName(),
            },
            new BuildOutputSinks { OutputSink = BuildOutputSink, BuildActionProgressReporter = bm => bms.ReportBuildMessages(new[] { _buildMessageMapper.Map<WorkspaceBuildMessage>(bm) }) },
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
