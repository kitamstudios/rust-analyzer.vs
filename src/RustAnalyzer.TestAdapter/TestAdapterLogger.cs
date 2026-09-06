using System;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

public sealed class TestAdapterLogger : LegacyLogger, IDisposable
{
    private readonly LegacyLoggerBridge _logger;
    private readonly LoggerFactory _loggerFactory;
    private readonly VSTestLoggerProvider _provider;

    public TestAdapterLogger(IMessageLogger logger)
    {
        _provider = new VSTestLoggerProvider(logger);
        _loggerFactory = new LoggerFactory(
            new[] { _provider, },
            new LoggerFilterOptions { MinLevel = LogLevel.Information, });
        _logger = new LegacyLoggerBridge(
            _loggerFactory.CreateLogger("KS.RustAnalyzer.TestAdapter.Legacy"));
    }

    public void WriteError(string format, params object[] args)
    {
        _logger.WriteError(format, args);
    }

    public void WriteLine(string format, params object[] args)
    {
        _logger.WriteLine(format, args);
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _provider.Dispose();
    }
}