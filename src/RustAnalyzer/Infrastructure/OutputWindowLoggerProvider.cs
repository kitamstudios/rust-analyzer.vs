using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Infrastructure;

[Export(typeof(OutputWindowLoggerProvider))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class OutputWindowLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private static readonly Guid OutputWindowPaneGuid = new("9142a5bb-c829-4d2a-87e3-9c7b545edf30");
    private static readonly string OutputWindowPaneName = Vsix.Name;
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly Func<Guid, string, IVsOutputWindowPane> _getOrCreatePane;
    private readonly Action<Exception> _observeFault;
    private readonly Func<Func<Task>, Task> _runOnMainThreadAsync;
    private int _disposed;
    private int _drainScheduled;
    private IVsOutputWindowPane _pane;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public OutputWindowLoggerProvider()
    {
        _getOrCreatePane = GetOrCreatePane;
        _observeFault = _ => { };
        _runOnMainThreadAsync = RunOnMainThreadAsync;
    }

    private OutputWindowLoggerProvider(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane,
        Action<Exception> observeFault)
    {
        _runOnMainThreadAsync = runOnMainThreadAsync;
        _getOrCreatePane = getOrCreatePane;
        _observeFault = observeFault;
    }

    [Import]
    public SVsServiceProvider ServiceProvider { get; set; }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new OutputWindowLogger(this, categoryName);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        DropQueuedEntries();
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    private static void ThrowIfFailed(int hresult, string operation)
    {
        if (ErrorHandler.Failed(hresult))
        {
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{hresult:X8}.");
        }
    }

    private bool IsEnabled(LogLevel logLevel)
    {
        return Volatile.Read(ref _disposed) == 0
            && logLevel >= LogLevel.Information
            && logLevel != LogLevel.None;
    }

    private IDisposable BeginScope<TState>(TState state)
    {
        return _scopeProvider.Push(state);
    }

    private void Log<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        if (!IsEnabled(logLevel) || formatter == null)
        {
            return;
        }

        try
        {
            var entry = MelLogEntryFormatter.Format(
                categoryName,
                logLevel,
                eventId,
                state,
                exception,
                formatter,
                _scopeProvider);
            _entries.Enqueue(
                $"{DateTime.Now:yyMMdd.HH.mm.ss.fff} - {entry}{Environment.NewLine}");
            ScheduleDrain();
        }
        catch (Exception e)
        {
            ObserveFault(e);
        }
    }

    private void ScheduleDrain()
    {
        if (Volatile.Read(ref _disposed) != 0
            || Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Observe(_runOnMainThreadAsync(DrainAsync));
        }
        catch (Exception e)
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
            DropQueuedEntries();
            ObserveFault(e);
        }
    }

    private Task DrainAsync()
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            while (_entries.TryDequeue(out var entry))
            {
                try
                {
                    WriteCore(entry);
                }
                catch (Exception e)
                {
                    ObserveFault(e);
                }
            }

            Interlocked.Exchange(ref _drainScheduled, 0);
            if (_entries.IsEmpty
                || Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0)
            {
                return Task.CompletedTask;
            }
        }

        Interlocked.Exchange(ref _drainScheduled, 0);
        DropQueuedEntries();
        return Task.CompletedTask;
    }

#pragma warning disable VSTHRD010
    private void WriteCore(string entry)
    {
        EnsurePane();
        var hresult = _pane.OutputStringThreadSafe(entry);
        ThrowIfFailed(hresult, "OutputWindowLoggerProvider.Write");
    }
#pragma warning restore VSTHRD010

    private void EnsurePane()
    {
        if (_pane == null)
        {
            _pane = _getOrCreatePane(OutputWindowPaneGuid, OutputWindowPaneName);
            if (_pane == null)
            {
                throw new InvalidOperationException(
                    "OutputWindowLoggerProvider.GetPane returned no pane.");
            }
        }
    }

    private IVsOutputWindowPane GetOrCreatePane(Guid paneId, string paneName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var outputWindow = ServiceProvider.GetService<SVsOutputWindow, IVsOutputWindow>();
        if (outputWindow == null)
        {
            throw new InvalidOperationException(
                "OutputWindowLoggerProvider.GetOutputWindow returned no service.");
        }

        var guid = paneId;
        var hresult = outputWindow.GetPane(ref guid, out var pane);
        if (ErrorHandler.Succeeded(hresult) && pane != null)
        {
            return pane;
        }

        hresult = outputWindow.CreatePane(ref guid, paneName, 0, 1);
        ThrowIfFailed(hresult, "OutputWindowLoggerProvider.CreatePane");
        hresult = outputWindow.GetPane(ref guid, out pane);
        ThrowIfFailed(hresult, "OutputWindowLoggerProvider.GetPane");
        return pane ?? throw new InvalidOperationException(
            "OutputWindowLoggerProvider.GetPane returned no pane.");
    }

    private void DropQueuedEntries()
    {
        while (_entries.TryDequeue(out _))
        {
        }
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
                task =>
                {
                    Interlocked.Exchange(ref _drainScheduled, 0);
                    DropQueuedEntries();
                    ObserveFault(task.Exception.GetBaseException());
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default)
            .Forget();
    }

    private static async Task RunOnMainThreadAsync(Func<Task> action)
    {
        await Task.Yield();
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
        await action();
    }

    private sealed class OutputWindowLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _categoryName;
        private readonly OutputWindowLoggerProvider _provider;

        public OutputWindowLogger(
            OutputWindowLoggerProvider provider,
            string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName ?? string.Empty;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _provider.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _provider.IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            _provider.Log(
                _categoryName,
                logLevel,
                eventId,
                state,
                exception,
                formatter);
        }
    }
}
