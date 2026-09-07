using System;
using System.ComponentModel.Composition;
using EnsureThat;
using Microsoft.Extensions.Logging;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter.Common;

[Export(typeof(ILogger))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class LegacyLoggerBridge : ILogger
{
    private const string LegacyCategory = "KS.RustAnalyzer.Legacy";
    private readonly MelLogger _logger;

    [ImportingConstructor]
    public LegacyLoggerBridge([Import] ILoggerFactory loggerFactory)
        : this(
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory))
                .CreateLogger(LegacyCategory))
    {
    }

    public LegacyLoggerBridge(MelLogger logger)
    {
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    public void WriteLine(string format, params object[] args)
    {
        TryLog(LogLevel.Information, format, args);
    }

    public void WriteError(string format, params object[] args)
    {
        TryLog(LogLevel.Error, format, args);
    }

    internal static MelLogger ToMelLogger(ILogger logger)
    {
        return new MelLoggerAdapter(EnsureArg.IsNotNull(logger, nameof(logger)));
    }

    private void TryLog(LogLevel logLevel, string format, object[] args)
    {
        try
        {
            _logger.Log(logLevel, new EventId(0), null, format, args);
        }
        catch (Exception)
        {
        }
    }

    private sealed class MelLoggerAdapter : MelLogger
    {
        private readonly ILogger _logger;

        public MelLoggerAdapter(ILogger logger)
        {
            _logger = logger;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return EmptyScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information
                && logLevel != LogLevel.None;
        }

        public void Log<TState>(
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
                if (logLevel >= LogLevel.Error)
                {
                    WriteError(state, exception, formatter);
                }
                else
                {
                    _logger.WriteLine("{0}", formatter(state, exception));
                }
            }
            catch (Exception)
            {
            }
        }

        private void WriteError<TState>(
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (exception == null)
            {
                _logger.WriteError("{0}", formatter(state, exception));
            }
            else
            {
                _logger.WriteError(
                    "{0} {1}",
                    formatter(state, exception),
                    exception);
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        private EmptyScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
