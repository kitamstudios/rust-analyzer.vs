using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class MetadataServiceFactoryLifecycleTests
{
    [Fact]
    public async Task ConstructionIsInertUntilLaterReadyAsync()
    {
        using var fixture = new PrerequisiteFixture();
        var toolchainCreations = 0;
        var watcherResolutions = 0;
        var toolchain = new Mock<IToolchainService>(MockBehavior.Strict);
        var watcher = CreateWatcher();
        var workspace = CreateWorkspace();
        var factory = fixture.CreateFactory(
            new Lazy<IToolchainService>(
                () =>
                {
                    toolchainCreations++;
                    return toolchain.Object;
                }));
        var created = factory.CreateService(
            workspace.Object,
            () =>
            {
                watcherResolutions++;
                return watcher.Object;
            },
            fixture.Context.Factory);
        var metadata = created.Should().BeAssignableTo<IMetadataService>().Which;
        var lifetime = created.Should().BeAssignableTo<IDisposable>().Which;

        toolchainCreations.Should().Be(0);
        watcherResolutions.Should().Be(0);
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().BeEmpty();
        workspace.VerifyGet(value => value.Location, Times.Never);

        await fixture.MakeReadyAsync();
        (await metadata.GetCachedPackagesAsync(default)).Should().BeEmpty();

        toolchainCreations.Should().Be(1);
        watcherResolutions.Should().Be(1);
        workspace.VerifyGet(value => value.Location, Times.Once);
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().ContainSingle();
        toolchain.VerifyNoOtherCalls();

        lifetime.Dispose();
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().BeEmpty();
    }

    [Fact]
    public async Task SuspendedConstructionRemainsInertAndUnavailableAsync()
    {
        using var fixture = new PrerequisiteFixture();
        await fixture.MakeSuspendedAsync();
        var toolchainCreations = 0;
        var watcherResolutions = 0;
        var factory = fixture.CreateFactory(
            new Lazy<IToolchainService>(
                () =>
                {
                    toolchainCreations++;
                    return Mock.Of<IToolchainService>();
                }));
        var watcher = CreateWatcher();
        var workspace = CreateWorkspace();
        var created = factory.CreateService(
            workspace.Object,
            () =>
            {
                watcherResolutions++;
                return watcher.Object;
            },
            fixture.Context.Factory);
        var metadata = created.Should().BeAssignableTo<IMetadataService>().Which;
        var lifetime = created.Should().BeAssignableTo<IDisposable>().Which;
        Func<Task> access = async () => await metadata.GetCachedPackagesAsync(default);

        await access.Should().ThrowAsync<InvalidOperationException>();

        toolchainCreations.Should().Be(0);
        watcherResolutions.Should().Be(0);
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().BeEmpty();
        workspace.VerifyGet(value => value.Location, Times.Never);
        lifetime.Dispose();
    }

    [Fact]
    public async Task CanceledEvaluationRetryInitializesOnceAsync()
    {
        using var fixture = new PrerequisiteFixture();
        var toolchainCreations = 0;
        var watcherResolutions = 0;
        var toolchain = new Mock<IToolchainService>(MockBehavior.Strict);
        var watcher = CreateWatcher();
        var factory = fixture.CreateFactory(
            new Lazy<IToolchainService>(
                () =>
                {
                    toolchainCreations++;
                    return toolchain.Object;
                }));
        var created = factory.CreateService(
            CreateWorkspace().Object,
            () =>
            {
                watcherResolutions++;
                return watcher.Object;
            },
            fixture.Context.Factory);
        var metadata = created.Should().BeAssignableTo<IMetadataService>().Which;
        var lifetime = created.Should().BeAssignableTo<IDisposable>().Which;
        var firstCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEvaluation = fixture.State.GetOrEvaluateAsync(
            _ => firstCompletion.Task,
            default);

        firstCompletion.SetCanceled();
        Func<Task> awaitFirst = async () => await firstEvaluation;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();

        toolchainCreations.Should().Be(0);
        watcherResolutions.Should().Be(0);

        await fixture.MakeReadyAsync();
        (await metadata.GetCachedPackagesAsync(default)).Should().BeEmpty();

        toolchainCreations.Should().Be(1);
        watcherResolutions.Should().Be(1);
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().ContainSingle();
        toolchain.VerifyNoOtherCalls();
        lifetime.Dispose();
    }

    [Fact]
    public async Task DisposalWinsPendingReadinessAsync()
    {
        using var fixture = new PrerequisiteFixture();
        var toolchainCreations = 0;
        var watcherResolutions = 0;
        var factory = fixture.CreateFactory(
            new Lazy<IToolchainService>(
                () =>
                {
                    toolchainCreations++;
                    return Mock.Of<IToolchainService>();
                }));
        var watcher = CreateWatcher();
        var created = factory.CreateService(
            CreateWorkspace().Object,
            () =>
            {
                watcherResolutions++;
                return watcher.Object;
            },
            fixture.Context.Factory);
        var metadata = created.Should().BeAssignableTo<IMetadataService>().Which;
        var lifetime = created.Should().BeAssignableTo<IDisposable>().Which;
        var pendingAccess = metadata.GetCachedPackagesAsync(default);

        pendingAccess.IsCompleted.Should().BeFalse();
        lifetime.Dispose();
        Func<Task> awaitPending = async () => await pendingAccess;
        await awaitPending.Should().ThrowAsync<OperationCanceledException>();
        await fixture.MakeReadyAsync();

        toolchainCreations.Should().Be(0);
        watcherResolutions.Should().Be(0);
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentConsumersInitializeMetadataOnceAsync()
    {
        using var fixture = new PrerequisiteFixture();
        await fixture.MakeReadyAsync();
        var toolchainCreations = 0;
        var watcherResolutions = 0;
        var toolchain = new Mock<IToolchainService>(MockBehavior.Strict);
        var watcher = CreateWatcher();
        var factory = fixture.CreateFactory(
            new Lazy<IToolchainService>(
                () =>
                {
                    Interlocked.Increment(ref toolchainCreations);
                    return toolchain.Object;
                }));
        var created = factory.CreateService(
            CreateWorkspace().Object,
            () =>
            {
                Interlocked.Increment(ref watcherResolutions);
                return watcher.Object;
            },
            fixture.Context.Factory);
        var metadata = created.Should().BeAssignableTo<IMetadataService>().Which;
        var lifetime = created.Should().BeAssignableTo<IDisposable>().Which;

        await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => metadata.GetCachedPackagesAsync(default)));

        toolchainCreations.Should().Be(1);
        watcherResolutions.Should().Be(1);
        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().ContainSingle();
        fixture.Telemetry.Events.Count(name => name == "CreatingMDS").Should().Be(1);
        toolchain.VerifyNoOtherCalls();
        lifetime.Dispose();
    }

    [Fact]
    public async Task DisposalQuiescesInFlightWatcherBeforeMetadataDisposalAsync()
    {
        using var fixture = new PrerequisiteFixture();
        await fixture.MakeReadyAsync();
        var toolchain = new Mock<IToolchainService>(MockBehavior.Strict);
        var watcher = CreateWatcher();
        var factory = fixture.CreateFactory(
            new Lazy<IToolchainService>(() => toolchain.Object));
        var created = factory.CreateService(
            CreateWorkspace().Object,
            () => watcher.Object,
            fixture.Context.Factory);
        var metadata = created.Should().BeAssignableTo<IMetadataService>().Which;
        var lifetime = created.Should().BeAssignableTo<IDisposable>().Which;
        await metadata.GetCachedPackagesAsync(default);
        using var fileSystemEvents = new BlockingFileSystemEvents();
        var callback = Task.Run(
            () => watcher.Object.OnBatchFileSystemChanged.InvokeAsync(
                this,
                new BatchFileSystemEventArgs(fileSystemEvents)));

        await fileSystemEvents.Entered;
        lifetime.Dispose();

        watcher.Object.OnBatchFileSystemChanged.AsyncInvocations.Should().BeEmpty();
        fixture.Telemetry.Events.Should().NotContain("DisposeMDS");

        fileSystemEvents.Release();
        await callback;

        fixture.Telemetry.Events.Count(name => name == "DisposeMDS").Should().Be(1);
        toolchain.VerifyNoOtherCalls();
    }

    private static Mock<IWorkspace> CreateWorkspace()
    {
        var workspace = new Mock<IWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(value => value.Location).Returns(@"C:\workspace");
        return workspace;
    }

    private static Mock<IFileWatcherService> CreateWatcher()
    {
        var watcher = new Mock<IFileWatcherService>(MockBehavior.Strict);
        watcher.SetupProperty(
            value => value.OnBatchFileSystemChanged,
            new AsyncEvent<BatchFileSystemEventArgs>());
        return watcher;
    }

    private sealed class BlockingFileSystemEvents : IEnumerable<FileSystemEventArgs>, IDisposable
    {
        private readonly TaskCompletionSource<object> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ManualResetEventSlim _release = new();

        public Task Entered => _entered.Task;

        public void Dispose()
        {
            _release.Dispose();
        }

        public IEnumerator<FileSystemEventArgs> GetEnumerator()
        {
            _entered.TrySetResult(null);
            _release.Wait();
            yield return new FileSystemEventArgs(
                WatcherChangeTypes.Changed,
                @"C:\workspace",
                "Cargo.toml");
        }

        public void Release()
        {
            _release.Set();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class PrerequisiteFixture : IDisposable
    {
        public PrerequisiteFixture()
        {
            Context = new JoinableTaskContext();
            State = new PrerequisiteProcessState(Context.Factory);
            Telemetry = new RecordingTelemetry();
            Policy = new PrerequisiteAvailabilityPolicy(
                State,
                Mock.Of<ILogger>(),
                Telemetry);
        }

        public JoinableTaskContext Context { get; }

        public PrerequisiteAvailabilityPolicy Policy { get; }

        public PrerequisiteProcessState State { get; }

        public RecordingTelemetry Telemetry { get; }

        public MetadataServiceFactory CreateFactory(Lazy<IToolchainService> cargoService)
        {
            return new MetadataServiceFactory
            {
                AvailabilityPolicy = Policy,
                CargoService = cargoService,
                L = Mock.Of<ILogger>(),
                T = Telemetry,
            };
        }

        public Task<PrerequisiteResult> MakeReadyAsync()
        {
            return State.GetOrEvaluateAsync(
                _ => Task.FromResult(PrerequisiteResult.Success),
                default);
        }

        public async Task MakeSuspendedAsync()
        {
            await State.GetOrEvaluateAsync(
                _ => Task.FromResult(
                    PrerequisiteResult.Failed(
                        new[]
                        {
                            new PrerequisiteFailure(
                                PrerequisiteFailureKind.CargoNotFound,
                                "Cargo was not found."),
                        })),
                default);
            State.Suspend();
        }

        public void Dispose()
        {
            Context.Dispose();
        }
    }

    private sealed class RecordingTelemetry : ITelemetryService
    {
        public ConcurrentQueue<string> Events { get; } = new();

        public void TrackEvent(
            string eventName,
            params (string Key, string Value)[] properties)
        {
            Events.Enqueue(eventName);
        }

        public void TrackException(Exception e, string siteName = null)
        {
        }

        public void TrackException(
            Exception e,
            (string Key, string Value)[] properties,
            string siteName = null)
        {
        }
    }
}
