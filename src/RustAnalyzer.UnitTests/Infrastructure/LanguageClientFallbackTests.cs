using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.LanguageService;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class LanguageClientFallbackTests
{
    [Fact]
    public async Task DownloadedStartupFailureRetriesPackagedOnceAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Client.Connections.Enqueue(null);
        fixture.Client.Connections.Enqueue(CreateConnection());

        var connection = await fixture.Client.ActivateAsync(default);
        await fixture.Client.OnServerInitializedAsync();

        connection.Should().NotBeNull();
        fixture.Client.StartedPaths.Should().Equal(
            fixture.DownloadedPath,
            fixture.PackagedPath);
        fixture.Downloader.Verify(
            service => service.ResetToPackagedAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.LanguageServerActivate
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    [Fact]
    public async Task DownloadedInitializationFailureRetriesPackagedOnceAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Client.Connections.Enqueue(CreateConnection());
        fixture.Client.Connections.Enqueue(CreateConnection());
        fixture.Client.StartAsync += async (_, _) =>
        {
            await fixture.Client.ActivateAsync(default);
        };

        await fixture.Client.ActivateAsync(default);
        var failure = await fixture.Client.OnServerInitializeFailedAsync(
            Mock.Of<ILanguageClientInitializationInfo>());
        await fixture.Client.OnServerInitializedAsync();

        failure.Should().BeNull();
        fixture.Client.StartedPaths.Should().Equal(
            fixture.DownloadedPath,
            fixture.PackagedPath);
        fixture.Downloader.Verify(
            service => service.ResetToPackagedAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.LanguageServerActivate
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    [Fact]
    public async Task PackagedRetryFailureIsTerminalAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Client.Connections.Enqueue(CreateConnection());
        fixture.Client.Connections.Enqueue(null);
        fixture.Client.StartAsync += async (_, _) =>
        {
            await fixture.Client.ActivateAsync(default);
        };

        await fixture.Client.ActivateAsync(default);
        var failure = await fixture.Client.OnServerInitializeFailedAsync(
            Mock.Of<ILanguageClientInitializationInfo>());
        var secondFailure = await fixture.Client.OnServerInitializeFailedAsync(
            Mock.Of<ILanguageClientInitializationInfo>());

        failure.Should().NotBeNull();
        secondFailure.Should().NotBeNull();
        fixture.Client.StartedPaths.Should().Equal(
            fixture.DownloadedPath,
            fixture.PackagedPath);
        fixture.Downloader.Verify(
            service => service.ResetToPackagedAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.LanguageServerActivate
                    && value.Outcome == UsageOutcome.Failed);
    }

    [Fact]
    public async Task FailureAfterInitializationDoesNotRetryAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Client.Connections.Enqueue(CreateConnection());

        await fixture.Client.ActivateAsync(default);
        await fixture.Client.OnServerInitializedAsync();
        var failure = await fixture.Client.OnServerInitializeFailedAsync(
            Mock.Of<ILanguageClientInitializationInfo>());

        failure.Should().NotBeNull();
        fixture.Client.StartedPaths.Should().Equal(fixture.DownloadedPath);
        fixture.Downloader.Verify(
            service => service.ResetToPackagedAsync(
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.LanguageServerActivate
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    private static Connection CreateConnection()
    {
        return new Connection(new MemoryStream(), new MemoryStream());
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            JoinableTaskContext context,
            Mock<IRlsInstallerService> downloader,
            TestLanguageClient client,
            RecordingFeatureUsageTelemetry telemetry,
            PathEx downloadedPath,
            PathEx packagedPath)
        {
            Context = context;
            Downloader = downloader;
            Client = client;
            Telemetry = telemetry;
            DownloadedPath = downloadedPath;
            PackagedPath = packagedPath;
        }

        public TestLanguageClient Client { get; }

        public JoinableTaskContext Context { get; }

        public PathEx DownloadedPath { get; }

        public Mock<IRlsInstallerService> Downloader { get; }

        public PathEx PackagedPath { get; }

        public RecordingFeatureUsageTelemetry Telemetry { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var context = new JoinableTaskContext();
            var state = new PrerequisiteProcessState(context.Factory);
            await state.GetOrEvaluateAsync(
                _ => Task.FromResult(PrerequisiteResult.Success),
                default);
            var downloadedPath =
                (PathEx)@"C:\extension\2026-09-07\rust-analyzer.exe";
            var packagedPath =
                (PathEx)@"C:\extension\2026-08-31\rust-analyzer.exe";
            var downloader = new Mock<IRlsInstallerService>(
                MockBehavior.Strict);
            downloader.SetupSequence(service => service.GetExePathAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(downloadedPath)
                .ReturnsAsync(packagedPath);
            downloader.Setup(service => service.IsPackagedExePath(
                    It.IsAny<PathEx>()))
                .Returns((PathEx path) => path == packagedPath);
            downloader.Setup(service => service.ResetToPackagedAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(packagedPath);
            var telemetry = new RecordingFeatureUsageTelemetry();
            var client = new TestLanguageClient(context.Factory)
            {
                AvailabilityPolicy = new PrerequisiteAvailabilityPolicy(
                    state,
                    Mock.Of<ILogger>()),
                L = Mock.Of<ILogger>(),
                RADownloader = downloader.Object,
                UsageTelemetry = telemetry,
            };
            return new Fixture(
                context,
                downloader,
                client,
                telemetry,
                downloadedPath,
                packagedPath);
        }

        public void Dispose()
        {
            Client.Dispose();
            Context.Dispose();
        }
    }

    private sealed class TestLanguageClient : LanguageClient
    {
        public TestLanguageClient(JoinableTaskFactory joinableTaskFactory)
            : base(joinableTaskFactory)
        {
        }

        public Queue<Connection> Connections { get; } = new();

        public List<PathEx> StartedPaths { get; } = new();

        protected override Task<Connection> StartServerAsync(
            PathEx serverPath,
            CancellationToken cancellationToken)
        {
            StartedPaths.Add(serverPath);
            return Task.FromResult(Connections.Dequeue());
        }
    }
}
