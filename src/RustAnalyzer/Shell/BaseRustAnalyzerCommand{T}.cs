using System;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using ShellInterop = Microsoft.VisualStudio.Shell.Interop;
using VSConstants = Microsoft.VisualStudio.VSConstants;

namespace KS.RustAnalyzer.Shell;

/// <summary>
/// NOTE: Consider adding visiblity constraints https://github.com/madskristensen/VisibilityConstraintsSample
/// TODO: Hide all this if Rust is not the current project.
/// </summary>
public abstract class BaseRustAnalyzerCommand<T> : BaseCommand<T>
    where T : class, new()
{
    private ILogger _logger;
    private ShellInterop.IVsSolution _solution;
    private ShellInterop.IVsDebugger _debugger;

    protected ILogger Logger => _logger ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<ILogger>();

    protected ShellInterop.IVsSolution Solution => _solution ??= Package.GetService<ShellInterop.SVsSolution, ShellInterop.IVsSolution>(false);

    protected ShellInterop.IVsDebugger Debugger => _debugger ??= Package.GetService<ShellInterop.SVsShellDebugger, ShellInterop.IVsDebugger>(false);

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Command.Visible = Command.Enabled = Command.Supported = IsCommandActive();
    }

    protected virtual bool IsCommandActive()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string workspaceRoot = null;
        if (ErrorHandler.Failed(Solution?.GetSolutionInfo(out workspaceRoot, out var _, out var _) ?? VSConstants.E_FAIL)
            || !(workspaceRoot + Constants.ManifestFileName2).FileExists())
        {
            return false;
        }

        var dbgMode = new ShellInterop.DBGMODE[1];
        if (ErrorHandler.Failed(Debugger?.GetMode(dbgMode) ?? VSConstants.E_FAIL))
        {
            return false;
        }

        return dbgMode[0] == ShellInterop.DBGMODE.DBGMODE_Design;
    }
}
