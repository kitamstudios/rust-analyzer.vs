using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Infrastructure;

[Export(typeof(IBuildOutputSink))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class BuildOutputSink : IBuildOutputSink
{
    private static readonly Guid BuildOutputPaneGuid = VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;
    private static readonly StringBuildMessagePreprocessor SbmPreprocessor = new();
    private readonly Func<Guid, string, IVsOutputWindowPane> _getOrCreatePane;
    private readonly Action<Exception> _observeFault;
    private readonly Func<Func<Task>, Task> _runOnMainThreadAsync;
    private IVsOutputWindowPane _buildOutputPane;

    public BuildOutputSink()
    {
        _getOrCreatePane = InitializeOutputPane;
        _observeFault = exception => T?.TrackException(exception);
        _runOnMainThreadAsync = RunOnMainThreadAsync;
    }

    private BuildOutputSink(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane,
        Action<Exception> observeFault)
    {
        _runOnMainThreadAsync = runOnMainThreadAsync;
        _getOrCreatePane = getOrCreatePane;
        _observeFault = observeFault;
    }

    [Import]
    private ITelemetryService T { get; set; }

    [Import]
    private SVsServiceProvider ServiceProvider { get; set; }

#pragma warning disable VSTHRD010
    public void WriteLine(PathEx rootPath, Func<BuildMessage, Task> buildOutputTaskReporter, BuildMessage message)
    {
        try
        {
            Observe(
                _runOnMainThreadAsync(
                    async () =>
                    {
                        Initialize();
                        ThrowIfFailed(_buildOutputPane.Activate(), "BuildOutputSink.Activate");

                        EnsureArg.IsTrue(
                            message is StringBuildMessage || message is DetailedBuildMessage,
                            nameof(message),
                            options => options.WithException(new ArgumentOutOfRangeException(nameof(message))));

                        if (message is StringBuildMessage sm)
                        {
                            if (string.IsNullOrEmpty(sm.Message))
                            {
                                return;
                            }

                            foreach (var msg in SbmPreprocessor.Preprocess(rootPath, sm.Message))
                            {
                                var hresult = _buildOutputPane.OutputStringThreadSafe(
                                    msg + Environment.NewLine);
                                ThrowIfFailed(hresult, "BuildOutputSink.Write");
                            }
                        }
                        else if (message is DetailedBuildMessage bm)
                        {
                            await buildOutputTaskReporter(bm);
                        }
                    }));
        }
        catch (Exception e)
        {
            ObserveFault(e);
        }
    }
#pragma warning restore VSTHRD010

#pragma warning disable VSTHRD010
    public void Clear()
    {
        try
        {
            Observe(
                _runOnMainThreadAsync(
                    () =>
                    {
                        Initialize();
                        ThrowIfFailed(_buildOutputPane.Clear(), "BuildOutputSink.Clear");
                        return Task.CompletedTask;
                    }));
        }
        catch (Exception e)
        {
            ObserveFault(e);
        }
    }
#pragma warning restore VSTHRD010

    private static void ThrowIfFailed(int hresult, string operation)
    {
        if (ErrorHandler.Failed(hresult))
        {
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{hresult:X8}.");
        }
    }

    private void Initialize()
    {
        if (IsInitialized())
        {
            return;
        }

        _buildOutputPane = _getOrCreatePane(BuildOutputPaneGuid, Vsix.Name);
        if (_buildOutputPane == null)
        {
            throw new InvalidOperationException(
                "BuildOutputSink.GetPane returned no pane.");
        }
    }

    private IVsOutputWindowPane InitializeOutputPane(Guid paneId, string title)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var outputWindow = ServiceProvider.GetService<SVsOutputWindow, IVsOutputWindow>();
        if (outputWindow == null)
        {
            throw new InvalidOperationException(
                "BuildOutputSink.GetOutputWindow returned no service.");
        }

        var hresult = outputWindow.GetPane(paneId, out var pane);
        if (ErrorHandler.Succeeded(hresult) && pane != null)
        {
            return pane;
        }

        hresult = outputWindow.CreatePane(
            paneId,
            title,
            fInitVisible: 1,
            fClearWithSolution: 1);
        ThrowIfFailed(hresult, "BuildOutputSink.CreatePane");
        hresult = outputWindow.GetPane(paneId, out pane);
        ThrowIfFailed(hresult, "BuildOutputSink.GetPane");
        return pane ?? throw new InvalidOperationException(
            "BuildOutputSink.GetPane returned no pane.");
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

    private bool IsInitialized()
    {
        return _buildOutputPane != null;
    }

    private static async Task RunOnMainThreadAsync(Func<Task> action)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
        await action();
    }
}
