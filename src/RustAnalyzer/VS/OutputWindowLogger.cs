using System;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.VS;

[Export(typeof(ILogger))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class OutputWindowLogger : ILogger
{
    private readonly string _name = Vsix.Name;
    private IVsOutputWindowPane _pane;

    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public SVsServiceProvider ServiceProvider { get; set; }

    public void WriteLine(string format, params object[] args)
    {
        try
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (EnsurePane())
                {
#pragma warning disable RS0030 // Do not used banned APIs
                    _pane.OutputString($"{DateTime.Now:yyyyMMdd HHmmss} - {string.Format(format, args)}\n");
#pragma warning restore RS0030 // Do not used banned APIs
                }
            });
        }
        catch (Exception e)
        {
            T.TrackException(e);
        }
    }

    private bool EnsurePane()
    {
        if (_pane == null)
        {
            var outputWindow = ServiceProvider.GetService<SVsOutputWindow, IVsOutputWindow>();
            Guid guid = new Guid("9142a5bb-c829-4d2a-87e3-9c7b545edf30");
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
            outputWindow.CreatePane(ref guid, _name, 1, 1);
            outputWindow.GetPane(ref guid, out _pane);
#pragma warning restore VSTHRD010 // Invoke single-threaded types on Main thread
        }

        return _pane != null;
    }
}