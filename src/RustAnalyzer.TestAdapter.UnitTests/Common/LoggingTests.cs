using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

[Trait("type", "UnitTests")]
public sealed class VSTestLoggerProviderTests
{
    [Fact]
    public void PreservesStructuredSemanticsAndMapsEnabledLevels()
    {
        var messageLogger = new RecordingMessageLogger();
        using var provider = new VSTestLoggerProvider(messageLogger);
        using var factory = CreateFactory(provider);
        var logger = factory.CreateLogger("Adapter.Discovery");
        var exception = new InvalidOperationException("failure detail");
        var scopes = Enumerable.Range(0, 10)
            .Select(index => logger.BeginScope("Scope {Index}", index))
            .ToArray();
        try
        {
            logger.LogTrace("trace");
            logger.LogDebug("debug");
            logger.LogInformation(
                new EventId(7, "Discovered"),
                "Processed {Crate}",
                "workspace");
            logger.LogWarning(new EventId(8, "Warning"), "Warning {Code}", 42);
            logger.LogError(
                new EventId(9, "Failed"),
                exception,
                "Failed {Crate}",
                "workspace");
            logger.LogCritical(new EventId(10, "Critical"), "Critical");
        }
        finally
        {
            foreach (var scope in scopes.Reverse())
            {
                scope.Dispose();
            }
        }

        messageLogger.Messages.Select(message => message.Level).Should().Equal(
            TestMessageLevel.Informational,
            TestMessageLevel.Warning,
            TestMessageLevel.Error,
            TestMessageLevel.Error);
        var information = messageLogger.Messages[0].Message;
        information.Should().Contain("[Information] Adapter.Discovery");
        information.Should().Contain("EventId=7(Discovered)");
        information.Should().Contain("Processed workspace");
        information.Should().Contain("Template: Processed {Crate}");
        information.Should().Contain("Properties: Crate=workspace");
        information.Should().Contain("Scope 0");
        information.Should().Contain("Scope 7");
        information.Should().Contain(" => ...");
        information.Should().NotContain("Scope 8");
        messageLogger.Messages[2].Message.Should().Contain(exception.ToString());
    }

    [Fact]
    public void SerializesConcurrentSends()
    {
        var messageLogger = new ConcurrentMessageLogger();
        using var provider = new VSTestLoggerProvider(messageLogger);
        using var factory = CreateFactory(provider);
        var logger = factory.CreateLogger("Adapter.Execution");

        Parallel.For(
            0,
            24,
            index => logger.LogInformation("Message {Index}", index));

        messageLogger.Messages.Should().HaveCount(24);
        messageLogger.Overlapped.Should().BeFalse();
    }

    [Fact]
    public void DisposalStopsLateMessagesAndFailuresDoNotEscape()
    {
        var recordingLogger = new RecordingMessageLogger();
        var provider = new VSTestLoggerProvider(recordingLogger);
        using var factory = CreateFactory(provider);
        var logger = factory.CreateLogger("Adapter.Lifetime");
        logger.LogInformation("before");

        provider.Dispose();
        logger.LogError("after");

        recordingLogger.Messages.Should().ContainSingle();

        using var failingProvider =
            new VSTestLoggerProvider(new ThrowingMessageLogger());
        using var failingFactory = CreateFactory(failingProvider);
        var productException = new InvalidOperationException("product failure");
        var failure = Record.Exception(
            () => failingFactory
                .CreateLogger("Adapter.Failure")
                .LogError(productException, "Product operation failed"));

        failure.Should().BeNull();
    }

    [Fact]
    public void CallbackContextSharesScopeAndStopsLateMessages()
    {
        var messageLogger = new RecordingMessageLogger();
        var contextType = typeof(TestAdapterLogger).Assembly.GetType(
            "KS.RustAnalyzer.TestAdapter.VSTestLoggingContext");
        contextType.Should().NotBeNull();
        var context = (IDisposable)Activator.CreateInstance(
            contextType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { messageLogger, "Execution" },
            null);
        var createLogger = contextType.GetMethod(
            "CreateLogger",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var legacyLoggerProperty = contextType.GetProperty(
            "LegacyLogger",
            BindingFlags.Instance | BindingFlags.NonPublic);
        createLogger.Should().NotBeNull();
        legacyLoggerProperty.Should().NotBeNull();
        var logger = (Microsoft.Extensions.Logging.ILogger)createLogger.Invoke(
            context,
            new object[] { typeof(TestExecutor) });
        var legacyLogger =
            (KS.RustAnalyzer.TestAdapter.Common.ILogger)legacyLoggerProperty.GetValue(context);

        logger.LogInformation(
            new EventId(21, "ContextMessage"),
            "Context message {Value}",
            42);
        legacyLogger.WriteLine("Legacy message {0}", 43);
        context.Dispose();
        logger.LogInformation("late");
        legacyLogger.WriteLine("late");

        messageLogger.Messages.Should().HaveCount(2);
        messageLogger.Messages.Select(message => message.Level).Should().Equal(
            TestMessageLevel.Informational,
            TestMessageLevel.Informational);
        messageLogger.Messages[0].Message.Should().Contain(
            "KS.RustAnalyzer.TestAdapter.TestExecutor EventId=21(ContextMessage)");
        messageLogger.Messages[0].Message.Should().Contain(
            "Template: Context message {Value}");
        messageLogger.Messages[0].Message.Should().Contain(
            "Properties: Value=42");
        messageLogger.Messages[0].Message.Should().Contain(
            "VSTest invocation Execution");
        messageLogger.Messages[1].Message.Should().Contain(
            "KS.RustAnalyzer.TestAdapter.Legacy EventId=0");
        messageLogger.Messages[1].Message.Should().Contain(
            "Legacy message 43");
        messageLogger.Messages[1].Message.Should().Contain(
            "VSTest invocation Execution");
    }

    [Fact]
    public void LegacyBridgeWritesExactlyOnceThroughMel()
    {
        var messageLogger = new RecordingMessageLogger();
        var logger = new TestAdapterLogger(messageLogger);

        logger.WriteLine("message {0} {1}", "value", 42);
        logger.WriteError("failure {0}", "detail");
        logger.Dispose();
        logger.WriteLine("late");

        messageLogger.Messages.Should().HaveCount(2);
        messageLogger.Messages[0].Level.Should().Be(
            TestMessageLevel.Informational);
        messageLogger.Messages[0].Message.Should().Contain("message value 42");
        messageLogger.Messages[1].Level.Should().Be(TestMessageLevel.Error);
        messageLogger.Messages[1].Message.Should().Contain("failure detail");
    }

    [Fact]
    public void UsesPinnedMel22Assemblies()
    {
        typeof(LoggerFactory).Assembly.GetName().Version.Should().Be(
            new Version(2, 2, 0, 0));
        typeof(Microsoft.Extensions.Logging.ILogger).Assembly
            .GetName()
            .Version
            .Should()
            .Be(new Version(2, 2, 0, 0));
    }

    private static LoggerFactory CreateFactory(ILoggerProvider provider)
    {
        return new LoggerFactory(
            new[] { provider, },
            new LoggerFilterOptions { MinLevel = LogLevel.Information, });
    }

    private sealed class RecordingMessageLogger : IMessageLogger
    {
        public List<(TestMessageLevel Level, string Message)> Messages { get; } =
            new();

        public void SendMessage(
            TestMessageLevel testMessageLevel,
            string message)
        {
            Messages.Add((testMessageLevel, message));
        }
    }

    private sealed class ConcurrentMessageLogger : IMessageLogger
    {
        private int _activeSend;
        private int _overlapped;

        public ConcurrentQueue<string> Messages { get; } = new();

        public bool Overlapped => Volatile.Read(ref _overlapped) != 0;

        public void SendMessage(
            TestMessageLevel testMessageLevel,
            string message)
        {
            if (Interlocked.Increment(ref _activeSend) != 1)
            {
                Interlocked.Exchange(ref _overlapped, 1);
            }

            Thread.Sleep(2);
            Messages.Enqueue(message);
            Interlocked.Decrement(ref _activeSend);
        }
    }

    private sealed class ThrowingMessageLogger : IMessageLogger
    {
        public void SendMessage(
            TestMessageLevel testMessageLevel,
            string message)
        {
            throw new InvalidOperationException("VSTest message failure.");
        }
    }
}
