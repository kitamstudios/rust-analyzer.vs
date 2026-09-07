using System;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

internal sealed class VSTestLoggingContext : IDisposable
{
    private const string LegacyCategory =
        "KS.RustAnalyzer.TestAdapter.Legacy";

    private readonly LoggerFactory _loggerFactory;
    private readonly VSTestLoggerProvider _provider;
    private readonly IDisposable _scope;

    internal VSTestLoggingContext(
        IMessageLogger messageLogger,
        string operation)
    {
        EnsureArg.IsNotNullOrEmpty(operation, nameof(operation));

        _provider = new VSTestLoggerProvider(messageLogger);
        _loggerFactory = new LoggerFactory(
            new[] { _provider, },
            new LoggerFilterOptions { MinLevel = LogLevel.Information, });
        LegacyLogger = new TestAdapterLogger(
            _loggerFactory.CreateLogger(LegacyCategory));
        _scope = _loggerFactory
            .CreateLogger<VSTestLoggingContext>()
            .BeginScope("VSTest invocation {Operation}", operation);
    }

    internal Common.ILogger LegacyLogger { get; }

    void IDisposable.Dispose()
    {
        TryDispose(_scope);
        TryDispose(_loggerFactory);
        TryDispose(_provider);
    }

    internal MelLogger CreateLogger(Type owner)
    {
        return _loggerFactory.CreateLogger(
            EnsureArg.IsNotNull(owner, nameof(owner)).FullName);
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception)
        {
        }
    }
}
