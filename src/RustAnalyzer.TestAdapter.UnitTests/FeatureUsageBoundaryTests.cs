using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

[Trait("type", "UnitTests")]
public sealed class FeatureUsageBoundaryTests
{
    [Fact]
    public void DiscoveryReportsOneSuccessfulTopLevelInvocation()
    {
        var telemetry = new RecordingFeatureUsageTelemetry();

        new TestDiscoverer(telemetry).DiscoverTests(
            Array.Empty<PathEx>(),
            Mock.Of<IDiscoveryContext>(),
            new NullMessageLogger(),
            Mock.Of<ITestCaseDiscoverySink>());

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.TestAdapterDiscover
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false, UsageOutcome.Failed)]
    [InlineData(true, UsageOutcome.Cancelled)]
    public void DiscoveryClassifiesFailedAndCancelledInvocations(
        bool cancelled,
        UsageOutcome expectedOutcome)
    {
        var telemetry = new RecordingFeatureUsageTelemetry();
        var exception = cancelled
            ? new OperationCanceledException()
            : (Exception)new InvalidOperationException();
        Action discover = () => new TestDiscoverer(telemetry).DiscoverTests(
            new ThrowingEnumerable<PathEx>(exception),
            Mock.Of<IDiscoveryContext>(),
            new NullMessageLogger(),
            Mock.Of<ITestCaseDiscoverySink>());

        discover.Should().Throw<Exception>();

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.TestAdapterDiscover
                    && value.Outcome == expectedOutcome);
    }

    [Fact]
    public void ExecutionReportsOneSuccessfulInvocationWithoutDiscovery()
    {
        var telemetry = new RecordingFeatureUsageTelemetry();

        new TestExecutor(telemetry).RunTests(
            Array.Empty<PathEx>(),
            Mock.Of<IRunContext>(),
            Mock.Of<IFrameworkHandle>());

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.TestAdapterExecute
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false, UsageOutcome.Failed)]
    [InlineData(true, UsageOutcome.Cancelled)]
    public void ExecutionClassifiesFailedAndCancelledInvocations(
        bool cancelled,
        UsageOutcome expectedOutcome)
    {
        var telemetry = new RecordingFeatureUsageTelemetry();
        var exception = cancelled
            ? new OperationCanceledException()
            : (Exception)new InvalidOperationException();
        Action execute = () => new TestExecutor(telemetry).RunTests(
            new ThrowingEnumerable<PathEx>(exception),
            Mock.Of<IRunContext>(),
            Mock.Of<IFrameworkHandle>());

        execute.Should().Throw<Exception>();

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.TestAdapterExecute
                    && value.Outcome == expectedOutcome);
    }

    [Theory]
    [InlineData(UsageOperation.CargoBuild)]
    [InlineData(UsageOperation.CargoClean)]
    [InlineData(UsageOperation.CargoClippy)]
    [InlineData(UsageOperation.CargoFormat)]
    public async Task CargoOperationsReportOneSuccessfulInvocationAsync(UsageOperation operation)
    {
        var telemetry = new RecordingFeatureUsageTelemetry();

        (await TrackCargoOperationAsync(
            telemetry,
            operation,
            default,
            () => Task.FromResult(true))).Should().BeTrue();

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == operation
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false, UsageOutcome.Failed)]
    [InlineData(true, UsageOutcome.Cancelled)]
    public async Task CargoOperationsClassifyFailedAndCancelledInvocationsAsync(
        bool cancelled,
        UsageOutcome expectedOutcome)
    {
        var telemetry = new RecordingFeatureUsageTelemetry();
        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            cancellation.Cancel();
        }

        (await TrackCargoOperationAsync(
            telemetry,
            UsageOperation.CargoBuild,
            cancellation.Token,
            () => Task.FromResult(false))).Should().BeFalse();

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.CargoBuild
                    && value.Outcome == expectedOutcome);
    }

    private static Task<bool> TrackCargoOperationAsync(
        IFeatureUsageTelemetry telemetry,
        UsageOperation operation,
        CancellationToken cancellationToken,
        Func<Task<bool>> execute)
    {
        var service = new ToolchainService(telemetry, Mock.Of<ILogger>());
        var method = typeof(ToolchainService).GetMethod(
            "TrackOperationAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (Task<bool>)method.Invoke(
            service,
            new object[] { operation, cancellationToken, execute });
    }

    private sealed class NullMessageLogger : IMessageLogger
    {
        public void SendMessage(TestMessageLevel testMessageLevel, string message)
        {
        }
    }

    private sealed class ThrowingEnumerable<T> : IEnumerable<T>
    {
        private readonly Exception _exception;

        public ThrowingEnumerable(Exception exception)
        {
            _exception = exception;
        }

        public IEnumerator<T> GetEnumerator()
        {
            throw _exception;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
