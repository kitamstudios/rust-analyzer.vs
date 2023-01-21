using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using EnsureThat;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.VS;

[Export(typeof(IOutputWindowPane))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class OutputWindowPane : IOutputWindowPane
{
    private static readonly Guid BuildOutputPaneGuid = VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;

    private readonly ConcurrentDictionary<int, IVsOutputWindowPane> _lazyOutputPaneCollection = new ();

    [Import]
    private SVsServiceProvider ServiceProvider { get; set; }

    public void WriteLine(string message)
    {
        if (!IsInitialized())
        {
            throw new InvalidOperationException("You need to initialize the output panes before using them.");
        }

#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
        _lazyOutputPaneCollection[0].Activate();
        var hr = _lazyOutputPaneCollection[0].OutputStringThreadSafe(message + Environment.NewLine);
#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread
        Ensure.That(ErrorHandler.Succeeded(hr));
    }

    public void Initialize()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!IsInitialized())
        {
            var crates = InitializeOutputPane("Rust (crates)", BuildOutputPaneGuid);
            _lazyOutputPaneCollection.TryAdd(0, crates);
        }
    }

    public void Clear()
    {
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
        _lazyOutputPaneCollection[0].Clear();
#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread
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
        return _lazyOutputPaneCollection.TryGetValue(0, out var crateWindow) && crateWindow != null;
    }
}
