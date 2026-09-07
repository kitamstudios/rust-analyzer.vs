using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using Moq;
using Xunit;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;
using OpenFolderTestContainer = KS.RustAnalyzer.TestAdapter.TestContainer;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class TestContainerLoggingTests
{
    [Fact]
    public async Task ProductionCompositionPreservesAllStructuredContainerBehaviorAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        var provider = new DisposeTrackingLoggerProvider();
        var factory = new LoggerFactory(new[] { provider, });
        var legacy = new Mock<LegacyLogger>(MockBehavior.Strict);
        var telemetry = new RecordingFeatureUsageTelemetry();
        var metadata = CreateMetadataService(
            Task.FromResult<IEnumerable<Workspace.Package>>(
                Array.Empty<Workspace.Package>()));
        var workspace = CreateWorkspace(metadata.Object);
        Exception workspaceFailure = null;
        var workspaceService = CreateWorkspaceService(
            () => workspaceFailure == null
                ? workspace.Object
                : throw workspaceFailure);
        var testContainerPath = CreateTestContainerFile();
        var discoverer = new TestContainerDiscoverer(
            () => workspaceService.Object,
            new TL
            {
                L = legacy.Object,
                T = telemetry,
            },
            factory,
            readiness.Policy,
            readiness.Context.Factory);

        try
        {
            var updates = 0;
            discoverer.TestContainersUpdated += (_, _) => updates++;
            await discoverer.Initialization;

            var package = new Workspace.Package
            {
                ManifestPath = (PathEx)@"C:\workspace\Cargo.toml",
                Name = "sample",
            };
            metadata.Raise(
                value => value.PackageAdded += null,
                this,
                package);
            metadata.Raise(
                value => value.PackageRemoved += null,
                this,
                package);
            metadata.Raise(
                value => value.TestContainerUpdated += null,
                this,
                testContainerPath);

            var container = discoverer.TestContainers
                .Should()
                .ContainSingle()
                .Which
                .Should()
                .BeOfType<OpenFolderTestContainer>()
                .Which;
            metadata.Raise(
                value => value.TestContainerUpdated += null,
                this,
                testContainerPath);

            var snapshot = container.Snapshot()
                .Should()
                .BeOfType<OpenFolderTestContainer>()
                .Which;
            snapshot.Source.Should().Be(container.Source);
            snapshot.TestContainerPath.Should().Be(container.TestContainerPath);
            snapshot.Discoverer.Should().BeSameAs(discoverer);
            snapshot.TL.Should().BeSameAs(container.TL);
            snapshot.CompareTo(container).Should().Be(0);

            File.Delete(testContainerPath);
            metadata.Raise(
                value => value.TestContainerUpdated += null,
                this,
                testContainerPath);
            metadata.Raise(
                value => value.TestContainerUpdated += null,
                this,
                testContainerPath);

            var expected =
                new InvalidOperationException("Workspace lookup failed.");
            workspaceFailure = expected;
            await workspaceService.Object.OnActiveWorkspaceChanged.InvokeAsync(
                this,
                EventArgs.Empty);

            updates.Should().Be(4);
            workspaceService.Object.OnActiveWorkspaceChanged
                .AsyncInvocations.Should().ContainSingle();
            metadata.VerifyAdd(
                value => value.PackageAdded +=
                    It.IsAny<EventHandler<Workspace.Package>>(),
                Times.Once);
            metadata.VerifyAdd(
                value => value.PackageRemoved +=
                    It.IsAny<EventHandler<Workspace.Package>>(),
                Times.Once);
            metadata.VerifyAdd(
                value => value.TestContainerUpdated +=
                    It.IsAny<EventHandler<PathEx>>(),
                Times.Once);
            metadata.VerifyRemove(
                value => value.PackageAdded -=
                    It.IsAny<EventHandler<Workspace.Package>>(),
                Times.Once);
            metadata.VerifyRemove(
                value => value.PackageRemoved -=
                    It.IsAny<EventHandler<Workspace.Package>>(),
                Times.Once);
            metadata.VerifyRemove(
                value => value.TestContainerUpdated -=
                    It.IsAny<EventHandler<PathEx>>(),
                Times.Once);

            discoverer.Dispose();
            provider.IsDisposed.Should().BeFalse();
            workspaceService.Object.OnActiveWorkspaceChanged
                .AsyncInvocations.Should().BeEmpty();

            File.WriteAllText(testContainerPath, "{}");
            var postDisposeSnapshot = container.Snapshot()
                .Should()
                .BeOfType<OpenFolderTestContainer>()
                .Which;
            postDisposeSnapshot.Source.Should().Be(container.Source);
            postDisposeSnapshot.Discoverer.Should().BeSameAs(discoverer);
            postDisposeSnapshot.TL.Should().BeSameAs(container.TL);

            var entries = provider.Recorder.Entries.ToArray();
            entries.Should().HaveCount(18);
            entries.Select(entry => entry.Category)
                .Distinct()
                .Should()
                .BeEquivalentTo(
                    typeof(OpenFolderTestContainer).FullName,
                    typeof(TestContainerDiscoverer).FullName);

            var created = AssertContract(
                entries,
                typeof(OpenFolderTestContainer),
                new EventId(1, "ContainerCreated"),
                LogLevel.Information,
                "New Test container {TestContainerPath} [{Timestamp}]");
            created.Should().HaveCount(4);
            created.Should().OnlyContain(entry =>
                entry.Properties["TestContainerPath"].Equals(
                    testContainerPath)
                && entry.Properties.ContainsKey("Timestamp"));
            var compared = AssertContract(
                entries,
                typeof(OpenFolderTestContainer),
                new EventId(2, "ContainerCompared"),
                LogLevel.Information,
                "Test container comparision {Timestamp} vs {OtherTimestamp} for {TestContainerPath}")
                .Should()
                .ContainSingle()
                .Which;
            compared.Properties["Timestamp"].Should().Be(container.TimeStamp);
            compared.Properties["OtherTimestamp"].Should().Be(
                container.TimeStamp);
            compared.Properties["TestContainerPath"].Should().Be(
                testContainerPath);

            var loading = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(1, "WorkspaceLoading"),
                LogLevel.Information,
                "TestContainerDiscoverer loading new workspace at '{WorkspaceLocation}'.")
                .Should()
                .ContainSingle()
                .Which;
            loading.Properties["WorkspaceLocation"].Should().Be(
                @"C:\workspace");
            var unloading = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(2, "WorkspaceUnloading"),
                LogLevel.Information,
                "Unloading workspace at '{WorkspaceLocation}'.");
            unloading.Should().HaveCount(3);
            unloading.Should().ContainSingle(entry =>
                Equals(
                    entry.Properties["WorkspaceLocation"],
                    @"C:\workspace"));
            var packageAdded = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(3, "PackageAdded"),
                LogLevel.Information,
                "TCD: Package Added EventHandler: '{ManifestPath}'")
                .Should()
                .ContainSingle()
                .Which;
            packageAdded.Properties["ManifestPath"].Should().Be(
                package.ManifestPath);
            var packageRemoved = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(4, "PackageRemoved"),
                LogLevel.Information,
                "TCD: Package Removed EventHandler: '{ManifestPath}'")
                .Should()
                .ContainSingle()
                .Which;
            packageRemoved.Properties["ManifestPath"].Should().Be(
                package.ManifestPath);
            var updated = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(5, "TestContainerUpdated"),
                LogLevel.Information,
                "TCD: TestContainer Updated EventHandler: '{TestContainerPath}'");
            updated.Should().HaveCount(4);
            updated.Should().OnlyContain(entry =>
                entry.Properties["TestContainerPath"].Equals(
                    testContainerPath));
            var addFailed = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(6, "TestContainerAddFailed"),
                LogLevel.Error,
                "TCD: Failed to add '{TestContainerPath}'")
                .Should()
                .ContainSingle()
                .Which;
            addFailed.Properties["TestContainerPath"].Should().Be(
                testContainerPath);
            var removeFailed = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(7, "TestContainerRemoveFailed"),
                LogLevel.Error,
                "TCD: Failed to remove container {TestContainerPath}.")
                .Should()
                .ContainSingle()
                .Which;
            removeFailed.Properties["TestContainerPath"].Should().Be(
                testContainerPath);
            var failure = AssertContract(
                entries,
                typeof(TestContainerDiscoverer),
                new EventId(8, "BackgroundOperationFailed"),
                LogLevel.Error,
                "Operation '{Operation}' failed unexpectedly.")
                .Should()
                .ContainSingle()
                .Which;
            failure.Properties["Operation"].Should().Be(
                "TestContainerDiscoverer.ActiveWorkspaceChangedEventHandlerAsync");
            failure.Exception.Should().BeSameAs(expected);
            entries.Where(entry => !ReferenceEquals(entry, failure))
                .Should()
                .OnlyContain(entry => entry.Exception == null);

            legacy.VerifyNoOtherCalls();
            telemetry.Events.Should().BeEmpty();

            factory.Dispose();
            provider.IsDisposed.Should().BeFalse();
            provider.Dispose();
            provider.IsDisposed.Should().BeTrue();
        }
        finally
        {
            discoverer.Dispose();
            factory.Dispose();
            provider.Dispose();
            File.Delete(testContainerPath);
        }
    }

    [Fact]
    public async Task SharedVsixFactoryKeepsContainerLogsOutOfBuildOutputAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        var writes = new ConcurrentQueue<string>();
        var drains = new ConcurrentQueue<Func<Task>>();
        var paneAcquisitions = new ConcurrentQueue<(Guid Id, string Name)>();
        var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
        pane.Setup(value => value.OutputStringThreadSafe(It.IsAny<string>()))
            .Callback((string value) => writes.Enqueue(value))
            .Returns(VSConstants.S_OK);
        using var provider = CreateOutputProvider(
            action =>
            {
                drains.Enqueue(action);
                return Task.CompletedTask;
            },
            (id, name) =>
            {
                paneAcquisitions.Enqueue((id, name));
                return pane.Object;
            });
        using var factory = new VsixLoggerFactory(provider);
        var telemetry = new RecordingFeatureUsageTelemetry();
        var workspaceService = CreateWorkspaceService(() => null);
        using var discoverer = new TestContainerDiscoverer(
            () => workspaceService.Object,
            new TL
            {
                L = Mock.Of<LegacyLogger>(),
                T = telemetry,
            },
            factory,
            readiness.Policy,
            readiness.Context.Factory);

        await discoverer.Initialization;
        await DrainAsync(drains);
        discoverer.Dispose();
        await DrainAsync(drains);

        var acquisition = paneAcquisitions.Should()
            .ContainSingle()
            .Which;
        acquisition.Id.Should().NotBe(
            VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid);
        writes.Should().HaveCount(2);
        writes.Should().OnlyContain(value =>
            value.Contains(typeof(TestContainerDiscoverer).FullName));
        pane.Verify(value => value.Activate(), Times.Never);
        telemetry.Events.Should().BeEmpty();

        var importingConstructor = typeof(TestContainerDiscoverer)
            .GetConstructors()
            .Single(constructor => constructor.GetCustomAttributes(
                    typeof(System.ComponentModel.Composition.ImportingConstructorAttribute),
                    false)
                .Any());
        importingConstructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(ILoggerFactory));
    }

    [Fact]
    public async Task DisposalCancellationSuppressesFaultAndDetachesBeforeLateMetadataAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var cacheRequested = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cachedPackages =
            new TaskCompletionSource<IEnumerable<Workspace.Package>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = CreateMetadataService(cachedPackages.Task);
        metadata.Setup(value => value.GetCachedPackagesAsync(
                It.IsAny<CancellationToken>()))
            .Callback(() => cacheRequested.TrySetResult(null))
            .Returns(cachedPackages.Task);
        var workspace = CreateWorkspace(metadata.Object);
        var workspaceService = CreateWorkspaceService(
            () => workspace.Object);
        var telemetry = new RecordingFeatureUsageTelemetry();
        var discoverer = new TestContainerDiscoverer(
            () => workspaceService.Object,
            new TL
            {
                L = Mock.Of<LegacyLogger>(),
                T = telemetry,
            },
            factory,
            readiness.Policy,
            readiness.Context.Factory);

        await cacheRequested.Task;
        discoverer.Dispose();
        cachedPackages.SetResult(Array.Empty<Workspace.Package>());
        await discoverer.Initialization;

        provider.Entries.Should().NotContain(entry =>
            entry.EventId == new EventId(
                8,
                "BackgroundOperationFailed"));
        workspaceService.Object.OnActiveWorkspaceChanged
            .AsyncInvocations.Should().BeEmpty();
        metadata.VerifyAdd(
            value => value.PackageAdded +=
                It.IsAny<EventHandler<Workspace.Package>>(),
            Times.Never);
        metadata.VerifyAdd(
            value => value.PackageRemoved +=
                It.IsAny<EventHandler<Workspace.Package>>(),
            Times.Never);
        metadata.VerifyAdd(
            value => value.TestContainerUpdated +=
                It.IsAny<EventHandler<PathEx>>(),
            Times.Never);
        telemetry.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task RetainedLegacyConstructorsRouteEachInvocationOnceAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        LegacyLogger legacy = new LegacyLoggerBridge(
            factory.CreateLogger("Legacy.TestContainers"));
        var telemetry = new RecordingFeatureUsageTelemetry();
        var tl = new TL
        {
            L = legacy,
            T = telemetry,
        };
        var workspaceService = CreateWorkspaceService(() => null);
        var discoverer = new TestContainerDiscoverer(
            () => workspaceService.Object,
            tl,
            readiness.Policy,
            readiness.Context.Factory);
        var testContainerPath = CreateTestContainerFile();

        try
        {
            await discoverer.Initialization;
            var container = new OpenFolderTestContainer(
                testContainerPath,
                Mock.Of<ITestContainerDiscoverer>(),
                tl);
            container.CompareTo(container).Should().Be(0);

            provider.Entries.Should().HaveCount(3);
            discoverer.Dispose();
            provider.Entries.Should().HaveCount(4);
            provider.Entries.Should().OnlyContain(entry =>
                entry.Category == "Legacy.TestContainers"
                && entry.EventId == new EventId(0, null)
                && entry.Template == "{0}");
            telemetry.Events.Should().BeEmpty();

            typeof(OpenFolderTestContainer).GetConstructor(
                    new[]
                    {
                        typeof(PathEx),
                        typeof(ITestContainerDiscoverer),
                        typeof(TL),
                    })
                .Should()
                .NotBeNull();
            typeof(TestContainerDiscoverer).GetConstructor(
                    new[]
                    {
                        typeof(Func<IVsFolderWorkspaceService>),
                        typeof(TL),
                        typeof(PrerequisiteAvailabilityPolicy),
                        typeof(JoinableTaskFactory),
                    })
                .Should()
                .NotBeNull();
            typeof(TestContainerDiscoverer).GetConstructor(
                    new[]
                    {
                        typeof(SVsServiceProvider),
                        typeof(LegacyLogger),
                        typeof(PrerequisiteAvailabilityPolicy),
                    })
                .Should()
                .NotBeNull();
        }
        finally
        {
            discoverer.Dispose();
            File.Delete(testContainerPath);
        }
    }

    private static RecordingLogEntry[] AssertContract(
        IEnumerable<RecordingLogEntry> entries,
        Type owner,
        EventId eventId,
        LogLevel level,
        string template)
    {
        eventId.Id.Should().BePositive();
        eventId.Name.Should().NotBeNullOrWhiteSpace();
        var matches = entries.Where(entry =>
                entry.Category == owner.FullName
                && entry.EventId == eventId)
            .ToArray();
        matches.Should().NotBeEmpty();
        matches.Should().OnlyContain(entry =>
            entry.Level == level
            && entry.Template == template);
        return matches;
    }

    private static Mock<IMetadataService> CreateMetadataService(
        Task<IEnumerable<Workspace.Package>> cachedPackages)
    {
        var metadata = new Mock<IMetadataService>();
        metadata.Setup(value => value.GetCachedPackagesAsync(
                It.IsAny<CancellationToken>()))
            .Returns(cachedPackages);
        return metadata;
    }

    private static Mock<IWorkspace> CreateWorkspace(
        IMetadataService metadataService)
    {
        var projectConfiguration =
            new Mock<IProjectConfigurationService>();
        projectConfiguration.Setup(value =>
                value.GetActiveProjectBuildConfiguration(
                    It.IsAny<ProjectTargetFileContext>()))
            .Returns("dev");
        var workspace = new Mock<IWorkspace>();
        workspace.SetupGet(value => value.Location)
            .Returns(@"C:\workspace");
        workspace.Setup(value => value.GetService(typeof(IMetadataService)))
            .Returns(metadataService);
        workspace.Setup(value => value.GetService(
                typeof(IProjectConfigurationService)))
            .Returns(projectConfiguration.Object);
        return workspace;
    }

    private static Mock<IVsFolderWorkspaceService> CreateWorkspaceService(
        Func<IWorkspace> getCurrentWorkspace)
    {
        var workspaceService =
            new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupGet(value => value.CurrentWorkspace)
            .Returns(getCurrentWorkspace);
        workspaceService.SetupProperty(
            value => value.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        return workspaceService;
    }

    private static PathEx CreateTestContainerFile()
    {
        var path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            $"t13-{Guid.NewGuid():N}{KS.RustAnalyzer.TestAdapter.Constants.TestsContainerExtension2}");
        File.WriteAllText(path, "{}");
        return (PathEx)path;
    }

    private static OutputWindowLoggerProvider CreateOutputProvider(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane)
    {
        var constructor = typeof(OutputWindowLoggerProvider).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(Func<Func<Task>, Task>),
                typeof(Func<Guid, string, IVsOutputWindowPane>),
                typeof(Action<Exception>),
            },
            null);
        constructor.Should().NotBeNull();
        return (OutputWindowLoggerProvider)constructor.Invoke(
            new object[]
            {
                runOnMainThreadAsync,
                getOrCreatePane,
                new Action<Exception>(_ => { }),
            });
    }

    private static async Task DrainAsync(
        ConcurrentQueue<Func<Task>> drains)
    {
        while (drains.TryDequeue(out var drain))
        {
            await drain();
        }
    }

    private sealed class DisposeTrackingLoggerProvider : ILoggerProvider
    {
        public bool IsDisposed { get; private set; }

        public RecordingLoggerProvider Recorder { get; } = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(
            string categoryName)
        {
            return Recorder.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ReadyFixture : IDisposable
    {
        private ReadyFixture()
        {
            Context = new JoinableTaskContext();
            State = new PrerequisiteProcessState(Context.Factory);
            Policy = new PrerequisiteAvailabilityPolicy(
                State,
                Mock.Of<LegacyLogger>());
        }

        public JoinableTaskContext Context { get; }

        public PrerequisiteAvailabilityPolicy Policy { get; }

        public PrerequisiteProcessState State { get; }

        public static async Task<ReadyFixture> CreateAsync()
        {
            var fixture = new ReadyFixture();
            await fixture.State.GetOrEvaluateAsync(
                _ => Task.FromResult(PrerequisiteResult.Success),
                default);
            return fixture;
        }

        public void Dispose()
        {
            Context.Dispose();
        }
    }
}
