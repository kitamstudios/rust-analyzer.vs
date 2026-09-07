using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using MelLogger = Microsoft.Extensions.Logging.ILogger;
using ShellInterop = Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Shell;

/// <summary>
/// NOTE: Consider adding visiblity constraints https://github.com/madskristensen/VisibilityConstraintsSample.
/// </summary>
public abstract class BaseRustAnalyzerCommand<T> : BaseCommand<T>
    where T : class, new()
{
    private readonly PrerequisiteProcessState _prerequisiteState;
    private ILoggerFactory _loggerFactory;
    private MelLogger _melLogger;
    private PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private IFeatureUsageTelemetry _usageTelemetry;
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

    protected ILoggerFactory LoggerFactory => _loggerFactory ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<ILoggerFactory>();

    protected MelLogger MelLogger =>
        _melLogger ??= LoggerFactory.CreateLogger(typeof(T).FullName);

    protected IFeatureUsageTelemetry UsageTelemetry => _usageTelemetry ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<IFeatureUsageTelemetry>();

    protected PrerequisiteAvailabilityPolicy AvailabilityPolicy =>
        _availabilityPolicy ??= Package.GetService<SComponentModel, IComponentModel2>(false)?.GetService<PrerequisiteAvailabilityPolicy>();

    protected ShellInterop.IVsSolution Solution => _solution ??= Package.GetService<ShellInterop.SVsSolution, ShellInterop.IVsSolution>(false);

    protected ShellInterop.IVsDebugger Debugger => _debugger ??= Package.GetService<ShellInterop.SVsShellDebugger, ShellInterop.IVsDebugger>(false);

    protected PrerequisiteProcessState PrerequisiteState => _prerequisiteState;

    protected static async Task TrackUsageAsync(
        IFeatureUsageTelemetry telemetry,
        UsageOperation operation,
        Func<Task<UsageOutcome>> execute)
    {
        var duration = Stopwatch.StartNew();
        try
        {
            var outcome = await execute();
            telemetry.Track(operation, outcome, duration.Elapsed);
        }
        catch (OperationCanceledException)
        {
            telemetry.Track(operation, UsageOutcome.Cancelled, duration.Elapsed);
            throw;
        }
        catch (Exception)
        {
            telemetry.Track(operation, UsageOutcome.Failed, duration.Elapsed);
            throw;
        }
    }

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

        try
        {
            ExecuteCore(sender, eventArgs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LogCommandExecutionFailed(e);
            throw;
        }
    }

    private void LogCommandExecutionFailed(Exception exception)
    {
        MelLogger.LogError(
            new EventId(1, "CommandExecutionFailed"),
            exception,
            "Operation '{Operation}' failed unexpectedly.",
            "BaseRustAnalyzerCommand.Execute");
    }
}
