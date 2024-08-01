using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using ShellInterop = Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Shell;

/// <summary>
/// NOTE: Consider adding visiblity constraints https://github.com/madskristensen/VisibilityConstraintsSample.
/// </summary>
public abstract class BaseRustAnalyzerCommand<T> : BaseCommand<T>
    where T : class, new()
{
    private ILogger _logger;
    private ITelemetryService _telemetry;
    private ShellInterop.IVsSolution _solution;
    private ShellInterop.IVsDebugger _debugger;

    protected BaseRustAnalyzerCommand()
    {
        CmdServices = new CmdServices(() => Package);
    }

    public CmdServices CmdServices { get; }

    protected ITelemetryService Telemetry => _telemetry ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<ITelemetryService>();

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

        var workspaceRoot = CmdServices.GetWorkspaceRoot();
        return (workspaceRoot + Constants.ManifestFileName2).FileExists() && CmdServices.IsIdeInDesignMode();
    }

    protected abstract void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs);

    /// <summary>
    /// NOTE: We dont use this.
    /// </summary>
    protected override Task ExecuteAsync(OleMenuCmdEventArgs eventArgs) => Task.CompletedTask;

    protected override void Execute(object sender, EventArgs ea)
    {
        Telemetry.TrackEvent(typeof(T).Name);

        try
        {
            ExecuteCore(sender, ea as OleMenuCmdEventArgs);
        }
        catch (Exception e)
        {
            Telemetry.TrackException(e, new[] { ("Command", typeof(T).Name) });
            throw;
        }
    }
}
