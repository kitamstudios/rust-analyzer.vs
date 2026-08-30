using System;
using System.Collections.Generic;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
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

[Trait("type", "UnitTests")]
public sealed class TelemetryFilterTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("1", null, false)]
    [InlineData(null, "exp", false)]
    public void ForwardsOnlyWhenCurrentFiltersAllow(
        string disabled,
        string rootSuffix,
        bool expectedForwarded)
    {
        const string disabledVariable = "RUSTANALYZER_TELEMETRY_DISABLED";
        const string rootSuffixVariable = "VSROOTSUFFIX";
        var originalDisabled = Environment.GetEnvironmentVariable(disabledVariable);
        var originalRootSuffix = Environment.GetEnvironmentVariable(rootSuffixVariable);
        try
        {
            Environment.SetEnvironmentVariable(disabledVariable, disabled);
            Environment.SetEnvironmentVariable(rootSuffixVariable, rootSuffix);
            var next = new RecordingTelemetryProcessor();
            var filter = new TelemetryService.FilterTelemetryProcessor(next);

            filter.Process(new EventTelemetry("owned-filter-test"));

            next.Items.Should().HaveCount(expectedForwarded ? 1 : 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(disabledVariable, originalDisabled);
            Environment.SetEnvironmentVariable(rootSuffixVariable, originalRootSuffix);
        }
    }

    private sealed class RecordingTelemetryProcessor : ITelemetryProcessor
    {
        public List<ITelemetry> Items { get; } = new();

        public void Process(ITelemetry item)
        {
            Items.Add(item);
        }
    }
}
