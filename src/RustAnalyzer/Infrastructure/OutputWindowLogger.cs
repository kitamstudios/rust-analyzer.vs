using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Infrastructure;

[Export(typeof(ILogger))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class OutputWindowLogger : ILogger
{
    private static readonly Guid OuputWidowPaneGuid = new("9142a5bb-c829-4d2a-87e3-9c7b545edf30");
    private static readonly string OuputWidowPaneName = Vsix.Name;
    private readonly Func<Guid, string, IVsOutputWindowPane> _getOrCreatePane;
    private readonly Action<Exception> _observeFault;
    private readonly Func<Func<Task>, Task> _runOnMainThreadAsync;
    private IVsOutputWindowPane _pane;

    public OutputWindowLogger()
    {
        _getOrCreatePane = GetOrCreatePane;
        _observeFault = exception => T?.TrackException(exception);
        _runOnMainThreadAsync = RunOnMainThreadAsync;
    }

    private OutputWindowLogger(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane,
        Action<Exception> observeFault)
    {
        _runOnMainThreadAsync = runOnMainThreadAsync;
        _getOrCreatePane = getOrCreatePane;
        _observeFault = observeFault;
    }

    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public SVsServiceProvider ServiceProvider { get; set; }

    public void WriteLine(string format, params object[] args)
    {
        QueueWrite(format, args);
    }

    public void WriteError(string format, params object[] args)
    {
        QueueWrite("[ERROR]: " + format, args);
    }

    private static void ThrowIfFailed(int hresult, string operation)
    {
        if (ErrorHandler.Failed(hresult))
        {
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{hresult:X8}.");
        }
    }

    private void QueueWrite(string format, object[] args)
    {
        try
        {
            Observe(
                _runOnMainThreadAsync(
                    () =>
                    {
                        WriteCore(format, args);
                        return Task.CompletedTask;
                    }));
        }
        catch (Exception e)
        {
            ObserveFault(e);
        }
    }

#pragma warning disable VSTHRD010
    private void WriteCore(string format, object[] args)
    {
        if (EnsurePane())
        {
            var hresult = _pane.OutputStringThreadSafe(
                $"{DateTime.Now:yyMMdd.HH.mm.ss.fff} - {string.Format(format, args)}\n");
            ThrowIfFailed(hresult, "OutputWindowLogger.Write");
        }
    }
#pragma warning restore VSTHRD010

    private bool EnsurePane()
    {
        if (_pane == null)
        {
            _pane = _getOrCreatePane(OuputWidowPaneGuid, OuputWidowPaneName);
            if (_pane == null)
            {
                throw new InvalidOperationException(
                    "OutputWindowLogger.GetPane returned no pane.");
            }
        }

        return true;
    }

    private IVsOutputWindowPane GetOrCreatePane(Guid paneId, string paneName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var outputWindow = ServiceProvider.GetService<SVsOutputWindow, IVsOutputWindow>();
        if (outputWindow == null)
        {
            throw new InvalidOperationException(
                "OutputWindowLogger.GetOutputWindow returned no service.");
        }

        var guid = paneId;
        var hresult = outputWindow.GetPane(ref guid, out var pane);
        if (ErrorHandler.Succeeded(hresult) && pane != null)
        {
            return pane;
        }

        hresult = outputWindow.CreatePane(ref guid, paneName, 1, 1);
        ThrowIfFailed(hresult, "OutputWindowLogger.CreatePane");
        hresult = outputWindow.GetPane(ref guid, out pane);
        ThrowIfFailed(hresult, "OutputWindowLogger.GetPane");
        return pane ?? throw new InvalidOperationException(
            "OutputWindowLogger.GetPane returned no pane.");
    }

    private void ObserveFault(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        try
        {
            _observeFault(exception);
        }
        catch (Exception)
        {
        }
    }

    private void Observe(Task operation)
    {
        operation.ContinueWith(
                task => ObserveFault(task.Exception.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default)
            .Forget();
    }

    private static async Task RunOnMainThreadAsync(Func<Task> action)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
        await action();
    }
}
