using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using EnsureThat;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.VS;

public enum OutputWindowTarget
{
    Cargo,
    Crate,
}

[Export(typeof(RustOutputPane))]
public sealed class RustOutputPane
{
    // This is the package manager pane that ships with VS2015, and we should print there if available.
    private static readonly Guid VSPackageManagerPaneGuid = new ("C7E31C31-1451-4E05-B6BE-D11B6829E8BB");
    private static readonly Guid CargoPaneGuid = VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;

    private readonly ConcurrentDictionary<OutputWindowTarget, IVsOutputWindowPane> _lazyOutputPaneCollection = new ();

    [Import]
    private SVsServiceProvider ServiceProvider { get; set; }

    public void WriteLine(string message, OutputWindowTarget target = OutputWindowTarget.Crate)
    {
        if (!IsInitialized())
        {
            throw new InvalidOperationException("You need to initialize the output panes before using them.");
        }

#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
        var hr = _lazyOutputPaneCollection[target].OutputStringThreadSafe(message + Environment.NewLine);
#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread
        Ensure.That(ErrorHandler.Succeeded(hr));
    }

    public void InitializeOutputPanes()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!IsInitialized())
        {
            var crates = InitializeOutputPane("Rust (crates)", CargoPaneGuid);
            _lazyOutputPaneCollection.TryAdd(OutputWindowTarget.Crate, crates);
            var cargoPane = InitializeOutputPane("Rust (cargo)", CargoPaneGuid);
            _lazyOutputPaneCollection.TryAdd(OutputWindowTarget.Cargo, cargoPane);
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

        // Must activate the workspace pane for it to show up in the output window
        lazyOutputPane.Activate();

        return lazyOutputPane;
    }

    private bool IsInitialized()
    {
        return _lazyOutputPaneCollection.TryGetValue(OutputWindowTarget.Crate, out var crateWindow) && crateWindow != null
            && _lazyOutputPaneCollection.TryGetValue(OutputWindowTarget.Cargo, out var cargoWindow) && cargoWindow != null;
    }
}
