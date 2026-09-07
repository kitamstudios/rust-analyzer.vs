using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.LanguageService;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using Moq;
using Xunit;
using MelEventId = Microsoft.Extensions.Logging.EventId;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class LanguageClientFallbackTests
{
    [Fact]
    public async Task NonStartingProcessUsesInformationFailureEventAsync()
    {
        using var context = new JoinableTaskContext();
        using var provider = new RecordingLoggerProvider();
        using var factory = new Microsoft.Extensions.Logging.LoggerFactory(
            new[] { provider, });
        using var client = new ProcessLanguageClient(context.Factory)
        {
            L = Mock.Of<ILogger>(),
            LoggerFactory = factory,
            WorkspaceService = Mock.Of<IVsFolderWorkspaceService>(),
        };
        var startProcess = typeof(LanguageClient).GetField(
            "_startProcess",
            BindingFlags.Instance | BindingFlags.NonPublic);
        startProcess.Should().NotBeNull();
        startProcess.SetValue(
            client,
            new Func<Process, bool>(_ => false));
        var serverPath = (PathEx)@"C:\extension\rust-analyzer.exe";

        var connection = await client.StartBaseServerAsync(
            serverPath,
            default);

        connection.Should().BeNull();
        provider.Entries.Should().HaveCount(2);
        var starting = GetEntry(
            provider,
            new MelEventId(2, "ServerStarting"),
            MelLogLevel.Information,
            "Starting rust-analyzer from path: {ServerPath}.");
        starting.Properties["ServerPath"].Should().Be(serverPath);
        var failed = GetEntry(
            provider,
            new MelEventId(3, "ServerStartFailed"),
            MelLogLevel.Information,
            "Error starting rust-analyzer from path.");
        failed.Exception.Should().BeNull();
    }

    [Fact]
    public async Task DownloadedStartExceptionUsesOwnerEventBeforePackagedRetryAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        var expected =
            new InvalidOperationException("Downloaded process failed.");
        fixture.Client.StartFailure = expected;
        fixture.Client.Connections.Enqueue(CreateConnection());

        using var connection = await fixture.Client.ActivateAsync(default);
        await fixture.Client.OnServerInitializedAsync();

        var entry = GetEntry(
            fixture.LoggerProvider,
            new MelEventId(5, "DownloadedServerStartFailed"),
            MelLogLevel.Error,
            "Downloaded rust-analyzer failed to start; retrying the packaged version.");
        entry.Exception.Should().BeSameAs(expected);
        fixture.LoggerProvider.Entries.Should().ContainSingle();
        fixture.Telemetry.Events.Should().ContainSingle()
            .Which.Outcome.Should().Be(UsageOutcome.Succeeded);
    }

    [Fact]
    public async Task PackagedRetryAndInitializationFailuresKeepNotificationAndTelemetrySeparateAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Client.Connections.Enqueue(CreateConnection());
        var retryException =
            new InvalidOperationException("Packaged reset failed.");
        fixture.Downloader
            .Setup(service => service.ResetToPackagedAsync(
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(retryException);
        var initializationException =
            new InvalidOperationException("LSP initialization failed.");
        var initialization = new Mock<ILanguageClientInitializationInfo>();
        initialization.SetupGet(value => value.InitializationException)
            .Returns(initializationException);

        using var connection = await fixture.Client.ActivateAsync(default);
        var failure = await fixture.Client.OnServerInitializeFailedAsync(
            initialization.Object);

        failure.FailureMessage.Should().Be(
            "Oh no! rust-analyzer failed to activate, now we can't test LSP! :(\n " +
            initializationException);
        fixture.LoggerProvider.Entries.Should().HaveCount(2);
        var retryFailed = GetEntry(
            fixture.LoggerProvider,
            new MelEventId(6, "PackagedServerRetryFailed"),
            MelLogLevel.Error,
            "Packaged rust-analyzer retry failed.");
        retryFailed.Exception.Should().BeSameAs(retryException);
        var initializationFailed = GetEntry(
            fixture.LoggerProvider,
            new MelEventId(1, "ServerInitializationFailed"),
            MelLogLevel.Information,
            "Oh no! rust-analyzer failed to activate, now we can't test LSP! :(");
        initializationFailed.Exception.Should()
            .BeSameAs(initializationException);
        initializationFailed.Properties.Should()
            .NotContainKey("InitializationException");
        fixture.Telemetry.Events.Should().ContainSingle()
            .Which.Outcome.Should().Be(UsageOutcome.Failed);
    }

    [Fact]
    public async Task ServerInitializationFailedPreservesExceptionOnLegacyRouteAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        var logger = new RecordingLogger();
        using var client = new LanguageClient(context.Factory)
        {
            AvailabilityPolicy = new PrerequisiteAvailabilityPolicy(
                state,
                logger),
            L = logger,
        };
        var expected =
            new InvalidOperationException("LSP initialization failed.");
        var initialization = new Mock<ILanguageClientInitializationInfo>();
        initialization.SetupGet(value => value.InitializationException)
            .Returns(expected);

        await client.OnServerInitializeFailedAsync(initialization.Object);

        logger.Errors.Should().BeEmpty();
        var delivery = logger.Lines.Should().ContainSingle().Which;
        delivery.Arguments.Should().ContainSingle(
            argument => ReferenceEquals(argument, expected));
        string.Format(delivery.Format, delivery.Arguments).Should().Be(
            "Oh no! rust-analyzer failed to activate, now we can't test LSP! :( " +
            expected);
    }

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

    private static RecordingLogEntry GetEntry(
        RecordingLoggerProvider provider,
        MelEventId eventId,
        MelLogLevel level,
        string template)
    {
        var entry = provider.Entries.Single(
            candidate => candidate.Category == typeof(LanguageClient).FullName
                && candidate.EventId == eventId);
        entry.Level.Should().Be(level);
        entry.Template.Should().Be(template);
        return entry;
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            JoinableTaskContext context,
            Mock<IRlsInstallerService> downloader,
            TestLanguageClient client,
            RecordingFeatureUsageTelemetry telemetry,
            RecordingLoggerProvider loggerProvider,
            Microsoft.Extensions.Logging.LoggerFactory loggerFactory,
            PathEx downloadedPath,
            PathEx packagedPath)
        {
            Context = context;
            Downloader = downloader;
            Client = client;
            Telemetry = telemetry;
            LoggerProvider = loggerProvider;
            LoggerFactory = loggerFactory;
            DownloadedPath = downloadedPath;
            PackagedPath = packagedPath;
        }

        public TestLanguageClient Client { get; }

        public JoinableTaskContext Context { get; }

        public Microsoft.Extensions.Logging.LoggerFactory LoggerFactory { get; }

        public RecordingLoggerProvider LoggerProvider { get; }

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
            var loggerProvider = new RecordingLoggerProvider();
            var loggerFactory =
                new Microsoft.Extensions.Logging.LoggerFactory(
                    new[] { loggerProvider, });
            var client = new TestLanguageClient(context.Factory)
            {
                AvailabilityPolicy = new PrerequisiteAvailabilityPolicy(
                    state,
                    loggerFactory.CreateLogger(
                        typeof(PrerequisiteAvailabilityPolicy).FullName)),
                L = Mock.Of<ILogger>(),
                LoggerFactory = loggerFactory,
                RADownloader = downloader.Object,
                UsageTelemetry = telemetry,
            };
            return new Fixture(
                context,
                downloader,
                client,
                telemetry,
                loggerProvider,
                loggerFactory,
                downloadedPath,
                packagedPath);
        }

        public void Dispose()
        {
            Client.Dispose();
            LoggerFactory.Dispose();
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

        public Exception StartFailure { get; set; }

        protected override Task<Connection> StartServerAsync(
            PathEx serverPath,
            CancellationToken cancellationToken)
        {
            StartedPaths.Add(serverPath);
            if (StartFailure != null)
            {
                var failure = StartFailure;
                StartFailure = null;
                return Task.FromException<Connection>(failure);
            }

            return Task.FromResult(Connections.Dequeue());
        }
    }

    private class ProcessLanguageClient : LanguageClient
    {
        public ProcessLanguageClient(
            JoinableTaskFactory joinableTaskFactory)
            : base(joinableTaskFactory)
        {
        }

        public Task<Connection> StartBaseServerAsync(
            PathEx serverPath,
            CancellationToken cancellationToken)
        {
            return StartServerAsync(serverPath, cancellationToken);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(string Format, object[] Arguments)> Errors { get; } =
            new();

        public List<(string Format, object[] Arguments)> Lines { get; } =
            new();

        public void WriteError(string format, params object[] args)
        {
            Errors.Add((format, args));
        }

        public void WriteLine(string format, params object[] args)
        {
            Lines.Add((format, args));
        }
    }
}
