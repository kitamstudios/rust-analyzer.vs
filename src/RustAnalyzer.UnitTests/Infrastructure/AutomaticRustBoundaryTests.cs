using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentAssertions;
using KS.RustAnalyzer.Debugger;
using KS.RustAnalyzer.Editor;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.LanguageService;
using KS.RustAnalyzer.NodeEnhancements;
using KS.RustAnalyzer.Shell;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Indexing;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class AutomaticRustBoundaryTests
{
    [Fact]
    public async Task LanguageClientDoesNotStartOrRaiseFailureUiWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var downloader = new Mock<IRlsInstallerService>(MockBehavior.Strict);
        using var client = new LanguageClient(unavailable.Context.Factory)
        {
            AvailabilityPolicy = unavailable.Policy,
            RADownloader = downloader.Object,
        };
        var starts = 0;
        client.StartAsync += (_, _) =>
        {
            starts++;
            return Task.CompletedTask;
        };

        client.ShowNotificationOnInitializeFailed.Should().BeFalse();
        await client.OnLoadedAsync();
        (await client.ActivateAsync(default)).Should().BeNull();
        (await client.OnServerInitializeFailedAsync(null)).Should().BeNull();

        starts.Should().Be(0);
        downloader.VerifyNoOtherCalls();
        unavailable.Logger.Lines.Should().ContainSingle();

        using var ready = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var expected = new InvalidOperationException("Language server path requested.");
        var readyDownloader = new Mock<IRlsInstallerService>(MockBehavior.Strict);
        readyDownloader.Setup(service => service.GetExePathAsync())
            .Returns(Task.FromException<PathEx>(expected));
        using var readyClient = new LanguageClient(ready.Context.Factory)
        {
            AvailabilityPolicy = ready.Policy,
            RADownloader = readyDownloader.Object,
        };
        var readyStarts = 0;
        readyClient.StartAsync += (_, _) =>
        {
            readyStarts++;
            return Task.CompletedTask;
        };

        readyClient.ShowNotificationOnInitializeFailed.Should().BeTrue();
        await readyClient.OnLoadedAsync();
        Func<Task> activate = async () => await readyClient.ActivateAsync(default);

        (await activate.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
        readyStarts.Should().Be(1);
        readyDownloader.Verify(service => service.GetExePathAsync(), Times.Once);
    }

    [Fact]
    public async Task LanguageClientStartsOnceAfterEvaluationBeginsLaterAsync()
    {
        using var fixture = new PrerequisiteFixture();
        using var client = new LanguageClient(fixture.Context.Factory)
        {
            AvailabilityPolicy = fixture.Policy,
        };
        var starts = 0;
        client.StartAsync += (_, _) =>
        {
            Interlocked.Increment(ref starts);
            return Task.CompletedTask;
        };

        var loads = Enumerable.Range(0, 32)
            .Select(_ => client.OnLoadedAsync())
            .ToArray();

        loads.Should().OnlyContain(load => !load.IsCompleted);
        await fixture.State.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        await Task.WhenAll(loads);

        starts.Should().Be(1);
    }

    [Fact]
    public async Task LanguageClientStartsAfterCanceledEvaluationRetryAsync()
    {
        using var fixture = new PrerequisiteFixture();
        using var client = new LanguageClient(fixture.Context.Factory)
        {
            AvailabilityPolicy = fixture.Policy,
        };
        var starts = 0;
        client.StartAsync += (_, _) =>
        {
            starts++;
            return Task.CompletedTask;
        };
        var loading = client.OnLoadedAsync();
        var firstCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEvaluation = fixture.State.GetOrEvaluateAsync(
            _ => firstCompletion.Task,
            default);

        firstCompletion.SetCanceled();
        Func<Task> awaitFirst = async () => await firstEvaluation;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();

        loading.IsCompleted.Should().BeFalse();
        await fixture.State.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        await loading;

        starts.Should().Be(1);
    }

    [Fact]
    public async Task LanguageClientStopWinsPendingReadinessAsync()
    {
        using var fixture = new PrerequisiteFixture();
        using var client = new LanguageClient(fixture.Context.Factory)
        {
            AvailabilityPolicy = fixture.Policy,
        };
        var starts = 0;
        var stops = 0;
        client.StartAsync += (_, _) =>
        {
            starts++;
            return Task.CompletedTask;
        };
        client.StopAsync += (_, _) =>
        {
            stops++;
            return Task.CompletedTask;
        };
        var loading = client.OnLoadedAsync();

        await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => client.StopServerAsync()));
        await loading;
        await fixture.State.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);

        starts.Should().Be(0);
        stops.Should().Be(1);
    }

    [Fact]
    public async Task MetadataWatcherDoesNotEnumerateOrUpdateWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var handler = new MetadataWorkspaceUpdateHandler(unavailable.Policy);

        await handler.HandleAsync(null, null, default);

        unavailable.Logger.Lines.Should().ContainSingle();

        using var ready = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(
                service => service.OnWorkspaceUpdateAsync(
                    It.IsAny<IEnumerable<PathEx>>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var readyHandler = new MetadataWorkspaceUpdateHandler(ready.Policy);

        await readyHandler.HandleAsync(
            new[] { (PathEx)@"C:\workspace\Cargo.toml", (PathEx)@"C:\workspace\ignored.txt" },
            metadata.Object,
            default);

        metadata.Verify(
            service => service.OnWorkspaceUpdateAsync(
                It.Is<IEnumerable<PathEx>>(paths => paths.Single().IsManifest()),
                default),
            Times.Once);
    }

    [Fact]
    public async Task FileScannerFactoryAndCallbacksDoNotResolveMetadataWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var factory = new FileScannerFactory
        {
            AvailabilityPolicy = unavailable.Policy,
            L = unavailable.Logger,
            T = unavailable.Telemetry,
        };

        var scanner = factory.CreateProvider(null);
        (await scanner.ScanContentAsync<object>(null, default)).Should().BeNull();
        (await ((IFileScannerUpToDateCheck)scanner)
            .IsUpToDateAsync(null, null, default, default)).Should().BeFalse();

        unavailable.Telemetry.Events.Should().BeEmpty();
        unavailable.Logger.Lines.Should().ContainSingle();

        using var ready = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var expected = new InvalidOperationException("Metadata requested.");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(
                service => service.GetContainingPackageAsync(
                    It.IsAny<PathEx>(),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>(expected));
        var readyScanner = new FileScanner(() => metadata.Object, ready.Policy);
        Func<Task> scan = async () =>
            await readyScanner.ScanContentAsync<object>(@"C:\workspace\src\lib.rs", default);

        (await scan.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
        metadata.VerifyAll();

        var workspace = new Mock<IWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(value => value.Location).Returns(@"C:\workspace");
        var readyFactory = new FileScannerFactory
        {
            AvailabilityPolicy = ready.Policy,
            L = ready.Logger,
            T = ready.Telemetry,
        };

        readyFactory.CreateProvider(workspace.Object).Should().NotBeNull();
        ready.Telemetry.Events.Should().ContainSingle().Which.Should().Be("Create Scanner");
    }

    [Fact]
    public async Task OpenFolderContextFactoryDoesNotResolveServicesWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var factory = new FileContextProviderFactory
        {
            AvailabilityPolicy = unavailable.Policy,
            CargoService = Mock.Of<IToolchainService>(),
            L = unavailable.Logger,
            OutputPane = Mock.Of<IBuildOutputSink>(),
            T = unavailable.Telemetry,
        };

        var provider = factory.CreateProvider(null);
        var contexts = await provider.GetContextsForFileAsync(null, default);

        contexts.Should().BeEmpty();
        unavailable.Telemetry.Events.Should().BeEmpty();
        unavailable.Logger.Lines.Should().ContainSingle();

        using var ready = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(
                service => service.GetContainingPackageAsync(
                    It.IsAny<PathEx>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync((KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package)null);
        var readyProvider = new FileContextProvider(
            () => metadata.Object,
            Mock.Of<IToolchainService>(),
            Mock.Of<IBuildOutputSink>(),
            () => throw new InvalidOperationException("Settings should not be requested."),
            ready.Policy);

        (await readyProvider.GetContextsForFileAsync(
            @"C:\workspace\Cargo.toml",
            default)).Should().BeEmpty();
        metadata.VerifyAll();

        var workspace = new Mock<IWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(value => value.Location).Returns(@"C:\workspace");
        var readyFactory = new FileContextProviderFactory
        {
            AvailabilityPolicy = ready.Policy,
            CargoService = Mock.Of<IToolchainService>(),
            L = ready.Logger,
            OutputPane = Mock.Of<IBuildOutputSink>(),
            T = ready.Telemetry,
        };

        readyFactory.CreateProvider(workspace.Object).Should().NotBeNull();
        ready.Telemetry.Events.Should().ContainSingle().Which.Should().Be("Create Context Provider");
    }

    [Fact]
    public async Task OpenFolderBuildAndCleanDoNotInvokeToolchainWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var toolchain = new Mock<IToolchainService>(MockBehavior.Strict);
        var target = new BuildTargetInfo();
        var output = Mock.Of<IBuildOutputSink>();
        var progress = Mock.Of<IBuildActionProgress>();
        var build = new BuildFileContext(toolchain.Object, target, output, unavailable.Policy);
        var clean = new CleanFileContext(toolchain.Object, target, output, unavailable.Policy);

        (await build.ExecuteBuildAsync(progress, default)).Should().BeFalse();
        (await clean.ExecuteBuildAsync(progress, default)).Should().BeFalse();

        toolchain.VerifyNoOtherCalls();
        unavailable.Logger.Lines.Should().HaveCount(2);

        using var ready = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var updates = 0;
        var commands = 0;
        var readyContext = new TestBuildFileContext(
            ready.Policy,
            () =>
            {
                updates++;
                return Task.CompletedTask;
            },
            (_, _, _) =>
            {
                commands++;
                return Task.FromResult(true);
            });

        (await readyContext.ExecuteBuildAsync(progress, default)).Should().BeTrue();
        updates.Should().Be(1);
        commands.Should().Be(1);
    }

    [Fact]
    public async Task NodeAndDebugProvidersReturnBeforeInspectingVisualStudioStateAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var nodeProvider = new NodeBrowseObjectProvider(
            unavailable.Telemetry,
            unavailable.Logger,
            unavailable.Policy);
        var debugProvider = new DebugLaunchTargetProvider
        {
            AvailabilityPolicy = unavailable.Policy,
            L = unavailable.Logger,
            T = unavailable.Telemetry,
        };

        nodeProvider.ProvideBrowseObject(null).Should().BeNull();
        debugProvider.SupportsContext(null, null).Should().BeFalse();
        debugProvider.LaunchDebugTarget(null, null, null);

        unavailable.Logger.Lines.Should().HaveCount(2);
        unavailable.Telemetry.Events.Should().BeEmpty();
        unavailable.Telemetry.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task RustTestHandoffDoesNotResolveOrSubscribeToWorkspaceWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var workspaceLookups = 0;
        using var discoverer = new TestContainerDiscoverer(
            () =>
            {
                workspaceLookups++;
                throw new InvalidOperationException("Workspace service should not be requested.");
            },
            new TL { L = unavailable.Logger, T = unavailable.Telemetry },
            unavailable.Policy,
            unavailable.Context.Factory);

        await discoverer.Initialization;

        workspaceLookups.Should().Be(0);
        discoverer.TestContainers.Should().BeEmpty();
        unavailable.Logger.Lines.Should().ContainSingle();

        using var ready = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var readyWorkspaceLookups = 0;
        var workspaceService = new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupGet(service => service.CurrentWorkspace).Returns((IWorkspace)null);
        workspaceService.SetupProperty(
            service => service.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        using var readyDiscoverer = new TestContainerDiscoverer(
            () =>
            {
                readyWorkspaceLookups++;
                return workspaceService.Object;
            },
            new TL { L = ready.Logger, T = ready.Telemetry },
            ready.Policy,
            ready.Context.Factory);

        await readyDiscoverer.Initialization;

        readyWorkspaceLookups.Should().Be(1);
        readyDiscoverer.TestContainers.Should().BeEmpty();
    }

    [Fact]
    public async Task TestDiscovererInitializesOnceAfterEvaluationBeginsLaterAsync()
    {
        using var fixture = new PrerequisiteFixture();
        var workspaceLookups = 0;
        var workspaceService = new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupGet(service => service.CurrentWorkspace).Returns((IWorkspace)null);
        workspaceService.SetupProperty(
            service => service.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        using var discoverer = new TestContainerDiscoverer(
            () =>
            {
                Interlocked.Increment(ref workspaceLookups);
                return workspaceService.Object;
            },
            new TL { L = fixture.Logger, T = fixture.Telemetry },
            fixture.Policy,
            fixture.Context.Factory);

        discoverer.Initialization.IsCompleted.Should().BeFalse();
        await fixture.State.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        await Task.WhenAll(Enumerable.Repeat(discoverer.Initialization, 32));

        workspaceLookups.Should().Be(1);
        workspaceService.Object.OnActiveWorkspaceChanged.AsyncInvocations.Should().ContainSingle();
    }

    [Fact]
    public async Task TestDiscovererInitializesAfterCanceledEvaluationRetryAsync()
    {
        using var fixture = new PrerequisiteFixture();
        var workspaceLookups = 0;
        var workspaceService = new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupGet(service => service.CurrentWorkspace).Returns((IWorkspace)null);
        workspaceService.SetupProperty(
            service => service.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        using var discoverer = new TestContainerDiscoverer(
            () =>
            {
                workspaceLookups++;
                return workspaceService.Object;
            },
            new TL { L = fixture.Logger, T = fixture.Telemetry },
            fixture.Policy,
            fixture.Context.Factory);
        var firstCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEvaluation = fixture.State.GetOrEvaluateAsync(
            _ => firstCompletion.Task,
            default);

        firstCompletion.SetCanceled();
        Func<Task> awaitFirst = async () => await firstEvaluation;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();

        discoverer.Initialization.IsCompleted.Should().BeFalse();
        await fixture.State.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        await discoverer.Initialization;

        workspaceLookups.Should().Be(1);
        workspaceService.Object.OnActiveWorkspaceChanged.AsyncInvocations.Should().ContainSingle();
    }

    [Fact]
    public async Task TestDiscovererDisposeWinsPendingReadinessAsync()
    {
        using var fixture = new PrerequisiteFixture();
        var workspaceLookups = 0;
        var workspaceService = new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupProperty(
            service => service.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        var discoverer = new TestContainerDiscoverer(
            () =>
            {
                workspaceLookups++;
                return workspaceService.Object;
            },
            new TL { L = fixture.Logger, T = fixture.Telemetry },
            fixture.Policy,
            fixture.Context.Factory);

        discoverer.Dispose();
        discoverer.Dispose();
        await discoverer.Initialization;
        await fixture.State.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);

        workspaceLookups.Should().Be(0);
        workspaceService.Object.OnActiveWorkspaceChanged.AsyncInvocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TestDiscovererDisposeDetachesWorkspaceAndMetadataSubscriptionsAsync()
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var metadataService = new RecordingMetadataService(
            Task.FromResult<IEnumerable<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>>(
                Array.Empty<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>()));
        var workspace = new Mock<IWorkspace>(MockBehavior.Loose);
        workspace.SetupGet(value => value.Location).Returns(@"C:\workspace");
        workspace.Setup(value => value.GetService(typeof(IMetadataService)))
            .Returns(metadataService);
        var workspaceService = new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupGet(service => service.CurrentWorkspace).Returns(workspace.Object);
        workspaceService.SetupProperty(
            service => service.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        var discoverer = new TestContainerDiscoverer(
            () => workspaceService.Object,
            new TL { L = fixture.Logger, T = fixture.Telemetry },
            fixture.Policy,
            fixture.Context.Factory);
        await discoverer.Initialization;

        workspaceService.Object.OnActiveWorkspaceChanged.AsyncInvocations.Should().ContainSingle();
        metadataService.PackageAddedSubscriptions.Should().Be(1);
        metadataService.PackageRemovedSubscriptions.Should().Be(1);
        metadataService.TestContainerUpdatedSubscriptions.Should().Be(1);

        discoverer.Dispose();

        workspaceService.Object.OnActiveWorkspaceChanged.AsyncInvocations.Should().BeEmpty();
        metadataService.PackageAddedSubscriptions.Should().Be(0);
        metadataService.PackageRemovedSubscriptions.Should().Be(0);
        metadataService.TestContainerUpdatedSubscriptions.Should().Be(0);
    }

    [Fact]
    public async Task TestDiscovererDisposeWinsPendingWorkspaceCallbackAsync()
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var cachedPackages =
            new TaskCompletionSource<IEnumerable<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var metadataService = new RecordingMetadataService(cachedPackages.Task);
        var workspace = new Mock<IWorkspace>(MockBehavior.Loose);
        workspace.SetupGet(value => value.Location).Returns(@"C:\workspace");
        workspace.Setup(value => value.GetService(typeof(IMetadataService)))
            .Returns(metadataService);
        var workspaceService = new Mock<IVsFolderWorkspaceService>(MockBehavior.Loose);
        workspaceService.SetupGet(service => service.CurrentWorkspace).Returns(workspace.Object);
        workspaceService.SetupProperty(
            service => service.OnActiveWorkspaceChanged,
            new AsyncEvent<EventArgs>());
        var discoverer = new TestContainerDiscoverer(
            () => workspaceService.Object,
            new TL { L = fixture.Logger, T = fixture.Telemetry },
            fixture.Policy,
            fixture.Context.Factory);

        await metadataService.CacheRequested;
        discoverer.Dispose();
        cachedPackages.SetResult(
            Array.Empty<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>());
        await discoverer.Initialization;

        workspaceService.Object.OnActiveWorkspaceChanged.AsyncInvocations.Should().BeEmpty();
        metadataService.PackageAddedSubscriptions.Should().Be(0);
        metadataService.PackageRemovedSubscriptions.Should().Be(0);
        metadataService.TestContainerUpdatedSubscriptions.Should().Be(0);
    }

    [Fact]
    public async Task UpdaterReturnsBeforeAnyReleaseOrRegistryAccessWhileUnavailableAsync()
    {
        using var unavailable = await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Suspended);
        var registry = new Mock<IRegistrySettingsService>(MockBehavior.Strict);
        var installer = new RlsInstallerService(
            registry.Object,
            unavailable.Telemetry,
            unavailable.Logger,
            unavailable.Policy);

        await installer.InstallLatestAsync();

        registry.VerifyNoOtherCalls();
        unavailable.Logger.Lines.Should().ContainSingle();
        unavailable.Telemetry.Events.Should().BeEmpty();
        unavailable.Telemetry.Exceptions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, 0)]
    [InlineData(PrerequisiteStatus.Evaluating, 0)]
    [InlineData(PrerequisiteStatus.Failed, 0)]
    [InlineData(PrerequisiteStatus.Suspended, 0)]
    [InlineData(PrerequisiteStatus.Ready, 1)]
    public async Task SwitchToolchainBeforeQueryStatusGuardsAllDownstreamEffectsAsync(
        PrerequisiteStatus status,
        int expectedDownstreamEffects)
    {
        using var fixture = new PrerequisiteFixture();
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PrerequisiteResult> evaluation = null;
        if (status == PrerequisiteStatus.Evaluating)
        {
            evaluation = fixture.State.GetOrEvaluateAsync(
                _ => evaluationCompletion.Task,
                default);
        }
        else if (status != PrerequisiteStatus.NotEvaluated)
        {
            await fixture.State.GetOrEvaluateAsync(
                _ => Task.FromResult(
                    status == PrerequisiteStatus.Ready
                        ? PrerequisiteResult.Success
                        : PrerequisiteResult.Failed(
                            new[]
                            {
                                new PrerequisiteFailure(
                                    PrerequisiteFailureKind.CargoNotFound,
                                    "Cargo was not found."),
                            })),
                default);
            if (status == PrerequisiteStatus.Suspended)
            {
                fixture.State.Suspend();
            }
        }

        fixture.State.Status.Should().Be(status);
        var downstreamEffects = new List<string>();

        RunSwitchToolchainBeforeQueryStatus(
            fixture.Policy,
            () => downstreamEffects.Add("enumerated rustup toolchains"));

        downstreamEffects.Should().HaveCount(expectedDownstreamEffects);
        if (evaluation != null)
        {
            evaluationCompletion.SetResult(PrerequisiteResult.Success);
            await evaluation;
        }
    }

    private static void RunSwitchToolchainBeforeQueryStatus(
        PrerequisiteAvailabilityPolicy availabilityPolicy,
        Action queryToolchains)
    {
        var constructor = typeof(SwitchToolchainCommand).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(PrerequisiteAvailabilityPolicy), typeof(Action), },
            null);
        constructor.Should().NotBeNull();
        var command = constructor.Invoke(new object[] { availabilityPolicy, queryToolchains, });
        var beforeQueryStatus = typeof(SwitchToolchainCommand).GetMethod(
            "BeforeQueryStatus",
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
        beforeQueryStatus.Should().NotBeNull();

        beforeQueryStatus.Invoke(command, new object[] { EventArgs.Empty, });
    }

    private sealed class TestBuildFileContext : BuildFileContextBase
    {
        public TestBuildFileContext(
            PrerequisiteAvailabilityPolicy availabilityPolicy,
            Func<Task> showUpdateNotificationAsync,
            Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> command)
            : base(
                new BuildTargetInfo(),
                Mock.Of<IBuildOutputSink>(),
                command,
                availabilityPolicy,
                AutomaticRustPath.OpenFolderBuild,
                showUpdateNotificationAsync)
        {
        }
    }

    private sealed class RecordingMetadataService : IMetadataService
    {
        private readonly Task<IEnumerable<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>> _cachedPackages;
        private readonly TaskCompletionSource<object> _cacheRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private EventHandler<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package> _packageAdded;
        private EventHandler<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package> _packageRemoved;
        private EventHandler<PathEx> _testContainerUpdated;

        public RecordingMetadataService(
            Task<IEnumerable<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>> cachedPackages)
        {
            _cachedPackages = cachedPackages;
        }

        public event EventHandler<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package> PackageAdded
        {
            add => _packageAdded += value;
            remove => _packageAdded -= value;
        }

        public event EventHandler<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package> PackageRemoved
        {
            add => _packageRemoved += value;
            remove => _packageRemoved -= value;
        }

        public event EventHandler<PathEx> TestContainerUpdated
        {
            add => _testContainerUpdated += value;
            remove => _testContainerUpdated -= value;
        }

        public int PackageAddedSubscriptions => _packageAdded?.GetInvocationList().Length ?? 0;

        public int PackageRemovedSubscriptions => _packageRemoved?.GetInvocationList().Length ?? 0;

        public int TestContainerUpdatedSubscriptions =>
            _testContainerUpdated?.GetInvocationList().Length ?? 0;

        public Task CacheRequested => _cacheRequested.Task;

        public Task<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package> GetPackageAsync(
            PathEx manifestPath,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package> GetContainingPackageAsync(
            PathEx filePath,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<int> OnWorkspaceUpdateAsync(
            IEnumerable<PathEx> filePaths,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package>> GetCachedPackagesAsync(
            CancellationToken cancellationToken)
        {
            _cacheRequested.TrySetResult(null);
            return _cachedPackages;
        }
    }

    private sealed class PrerequisiteFixture : IDisposable
    {
        public PrerequisiteFixture()
        {
            Context = new JoinableTaskContext();
            State = new PrerequisiteProcessState(Context.Factory);
            Logger = new RecordingLogger();
            Telemetry = new RecordingTelemetry();
            Policy = new PrerequisiteAvailabilityPolicy(State, Logger, Telemetry);
        }

        public JoinableTaskContext Context { get; }

        public RecordingLogger Logger { get; }

        public PrerequisiteAvailabilityPolicy Policy { get; }

        public PrerequisiteProcessState State { get; }

        public RecordingTelemetry Telemetry { get; }

        public static async Task<PrerequisiteFixture> CreateAsync(PrerequisiteStatus status)
        {
            var fixture = new PrerequisiteFixture();
            EnsureArg.IsTrue(
                status == PrerequisiteStatus.Ready ||
                    status == PrerequisiteStatus.Suspended,
                nameof(status),
                options => options.WithException(
                    new ArgumentOutOfRangeException(nameof(status))));
            if (status == PrerequisiteStatus.Ready)
            {
                await fixture.State.GetOrEvaluateAsync(
                    _ => Task.FromResult(PrerequisiteResult.Success),
                    default);
            }
            else if (status == PrerequisiteStatus.Suspended)
            {
                await fixture.State.GetOrEvaluateAsync(
                    _ => Task.FromResult(
                        PrerequisiteResult.Failed(
                            new[]
                            {
                                new PrerequisiteFailure(
                                    PrerequisiteFailureKind.CargoNotFound,
                                    "Cargo was not found."),
                            })),
                    default);
                fixture.State.Suspend();
            }

            return fixture;
        }

        public void Dispose()
        {
            Context.Dispose();
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public ConcurrentQueue<(string Format, object[] Arguments)> Errors { get; } = new();

        public ConcurrentQueue<(string Format, object[] Arguments)> Lines { get; } = new();

        public void WriteLine(string format, params object[] args)
        {
            Lines.Enqueue((format, args));
        }

        public void WriteError(string format, params object[] args)
        {
            Errors.Enqueue((format, args));
        }
    }

    private sealed class RecordingTelemetry : ITelemetryService
    {
        public ConcurrentQueue<string> Events { get; } = new();

        public ConcurrentQueue<Exception> Exceptions { get; } = new();

        public void TrackEvent(string eventName, params (string Key, string Value)[] properties)
        {
            Events.Enqueue(eventName);
        }

        public void TrackException(Exception e, string siteName = null)
        {
            Exceptions.Enqueue(e);
        }

        public void TrackException(
            Exception e,
            (string Key, string Value)[] properties,
            string siteName = null)
        {
            Exceptions.Enqueue(e);
        }
    }
}
