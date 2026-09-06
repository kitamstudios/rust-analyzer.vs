using System;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public sealed class VSTestLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _sync = new();
    private readonly IMessageLogger _messageLogger;
    private bool _disposed;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public VSTestLoggerProvider(IMessageLogger messageLogger)
    {
        _messageLogger = EnsureArg.IsNotNull(messageLogger, nameof(messageLogger));
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new VSTestLogger(this, categoryName);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    private bool IsEnabled(LogLevel logLevel)
    {
        lock (_sync)
        {
            return !_disposed
                && logLevel >= LogLevel.Information
                && logLevel != LogLevel.None;
        }
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
        if (formatter == null)
        {
            return;
        }

        try
        {
            var message = MelLogEntryFormatter.Format(
                categoryName,
                logLevel,
                eventId,
                state,
                exception,
                formatter,
                _scopeProvider);
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _messageLogger.SendMessage(MapLevel(logLevel), message);
            }
        }
        catch (Exception)
        {
        }
    }

    private static TestMessageLevel MapLevel(LogLevel logLevel)
    {
        switch (logLevel)
        {
            case LogLevel.Warning:
                return TestMessageLevel.Warning;
            case LogLevel.Error:
            case LogLevel.Critical:
                return TestMessageLevel.Error;
            default:
                return TestMessageLevel.Informational;
        }
    }

    private sealed class VSTestLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _categoryName;
        private readonly VSTestLoggerProvider _provider;

        public VSTestLogger(VSTestLoggerProvider provider, string categoryName)
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
            if (!IsEnabled(logLevel))
            {
                return;
            }

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
