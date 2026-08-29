using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
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
    private readonly PrerequisiteProcessState _prerequisiteState;
    private ILogger _logger;
    private PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private ITelemetryService _telemetry;
    private ShellInterop.IVsSolution _solution;
    private ShellInterop.IVsDebugger _debugger;

    protected BaseRustAnalyzerCommand()
        : this(PrerequisiteProcessState.Current)
    {
    }

    protected BaseRustAnalyzerCommand(PrerequisiteProcessState prerequisiteState)
    {
        _prerequisiteState = EnsureArg.IsNotNull(
            prerequisiteState,
            nameof(prerequisiteState),
            options => options.WithException(
                new ArgumentNullException(nameof(prerequisiteState))));
        CmdServices = new CmdServices(() => Package);
    }

    public CmdServices CmdServices { get; }

    protected ITelemetryService Telemetry => _telemetry ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<ITelemetryService>();

    protected ILogger Logger => _logger ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<ILogger>();

    protected PrerequisiteAvailabilityPolicy AvailabilityPolicy =>
        _availabilityPolicy ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<PrerequisiteAvailabilityPolicy>();

    protected ShellInterop.IVsSolution Solution => _solution ??= Package.GetService<ShellInterop.SVsSolution, ShellInterop.IVsSolution>(false);

    protected ShellInterop.IVsDebugger Debugger => _debugger ??= Package.GetService<ShellInterop.SVsShellDebugger, ShellInterop.IVsDebugger>(false);

    protected PrerequisiteProcessState PrerequisiteState => _prerequisiteState;

    protected sealed override void BeforeQueryStatus(EventArgs e)
    {
        if (!_prerequisiteState.IsAvailable)
        {
            Command.Visible = Command.Enabled = Command.Supported = false;
            BeforeQueryStatusUnavailable(e);
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

    protected virtual void BeforeQueryStatusUnavailable(EventArgs e)
    {
    }

    protected virtual bool IsCommandActive()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var workspaceRoot = CmdServices.GetWorkspaceRoot();
        return (workspaceRoot + Constants.ManifestFileName2).FileExists() && CmdServices.IsIdeInDesignMode();
    }

    protected abstract void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs);

    protected virtual void ExecuteUnavailable(object sender, OleMenuCmdEventArgs eventArgs)
    {
    }

    /// <summary>
    /// NOTE: We dont use this.
    /// </summary>
    protected override Task ExecuteAsync(OleMenuCmdEventArgs eventArgs) => Task.CompletedTask;

    protected override void Execute(object sender, EventArgs ea)
    {
        var eventArgs = ea as OleMenuCmdEventArgs;
        if (!_prerequisiteState.IsAvailable)
        {
            ExecuteUnavailable(sender, eventArgs);
            return;
        }

        Telemetry.TrackEvent(typeof(T).Name);

        try
        {
            ExecuteCore(sender, eventArgs);
        }
        catch (Exception e)
        {
            Telemetry.TrackException(e, new[] { ("Command", typeof(T).Name) });
            throw;
        }
    }
}
