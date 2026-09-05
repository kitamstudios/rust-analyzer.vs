using System.Collections.Generic;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

[Trait("type", "UnitTests")]
public sealed class TestAdapterLoggerTests
{
    [Theory]
    [InlineData(false, TestMessageLevel.Informational)]
    [InlineData(true, TestMessageLevel.Error)]
    public void MapsAndFormatsMessages(bool writeError, TestMessageLevel expectedLevel)
    {
        var messageLogger = new RecordingMessageLogger();
        var logger = new TestAdapterLogger(messageLogger);

        if (writeError)
        {
            logger.WriteError("message {0} {1}", "value", 42);
        }
        else
        {
            logger.WriteLine("message {0} {1}", "value", 42);
        }

        messageLogger.Messages.Should().Equal(
            (expectedLevel, "ra.vs> message value 42"));
    }

    private sealed class RecordingMessageLogger : IMessageLogger
    {
        public List<(TestMessageLevel Level, string Message)> Messages { get; } = new();

        public void SendMessage(TestMessageLevel testMessageLevel, string message)
        {
            Messages.Add((testMessageLevel, message));
        }
    }
}
