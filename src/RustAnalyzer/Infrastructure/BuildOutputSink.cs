using System;
using System.ComponentModel.Composition;
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
    private IVsOutputWindowPane _buildOutputPane;

    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    private SVsServiceProvider ServiceProvider { get; set; }

    public void WriteLine(Func<BuildMessage, Task> buildOutputTaskReporter, BuildMessage message)
    {
        try
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Initialize();

                if (message is StringBuildMessage sm)
                {
                    if (string.IsNullOrEmpty(sm.Message))
                    {
                        return;
                    }

                    _buildOutputPane.Activate();
                    var hr = _buildOutputPane.OutputStringThreadSafe(sm.Message + Environment.NewLine);
                    Ensure.That(ErrorHandler.Succeeded(hr));
                }
                else if (message is DetailedBuildMessage bm)
                {
                    await buildOutputTaskReporter(bm);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(message));
                }
            });
        }
        catch (Exception e)
        {
            T.TrackException(e);
        }
    }

    public void Clear()
    {
        try
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Initialize();
                _buildOutputPane.Clear();
            });
        }
        catch (Exception e)
        {
            T.TrackException(e);
        }
    }

    private void Initialize()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!IsInitialized())
        {
            _buildOutputPane = InitializeOutputPane("Rust (cargo)", BuildOutputPaneGuid);
        }
    }

    private IVsOutputWindowPane InitializeOutputPane(string title, Guid paneId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var outputWindow = ServiceProvider.GetService<SVsOutputWindow, IVsOutputWindow>();

        // Try to get the workspace pane if it has already been registered
        var hr = outputWindow.GetPane(paneId, out var lazyOutputPane);

        // If the workspace pane has not been registered before, create it
        if (lazyOutputPane == null || ErrorHandler.Failed(hr))
        {
            if (ErrorHandler.Failed(outputWindow.CreatePane(paneId, title, fInitVisible: 1, fClearWithSolution: 1)) ||
                ErrorHandler.Failed(outputWindow.GetPane(paneId, out lazyOutputPane)))
            {
                return null;
            }
        }

        return lazyOutputPane;
    }

    private bool IsInitialized()
    {
        return _buildOutputPane != null;
    }
}
