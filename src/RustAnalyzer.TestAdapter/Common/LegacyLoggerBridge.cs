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
}
