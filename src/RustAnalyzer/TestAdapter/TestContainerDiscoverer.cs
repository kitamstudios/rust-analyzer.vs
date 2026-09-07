using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

[Export(typeof(ITestContainerDiscoverer))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TestContainerDiscoverer : ITestContainerDiscoverer, IDisposable
{
    private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private readonly ConcurrentDictionary<PathEx, TestContainer> _testContainersCache = new();
    private readonly Func<IVsFolderWorkspaceService> _getWorkspaceFactory;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly MelLogger _logger;
    private readonly object _sync = new();
    private readonly MelLogger _testContainerLogger;
    private readonly TL _tl;
    private readonly SemaphoreSlim _workspaceChangeGate = new(1, 1);
    private IMetadataService _currentMetadataService;
    private IWorkspace _currentWorkspace;
    private bool _disposed;
    private IVsFolderWorkspaceService _workspaceFactory;

    [ImportingConstructor]
    public TestContainerDiscoverer(
        [Import] SVsServiceProvider serviceProvider,
        [Import] ILogger l,
        [Import] ILoggerFactory loggerFactory,
        [Import] PrerequisiteAvailabilityPolicy availabilityPolicy)
        : this(
            () => VS.GetRequiredService<SComponentModel, IComponentModel>()
                .GetService<IVsFolderWorkspaceService>(),
            new TL
            {
                L = l,
            },
            loggerFactory,
            availabilityPolicy,
            RustAnalyzerPackage.JTF)
    {
    }

    public TestContainerDiscoverer(
        SVsServiceProvider serviceProvider,
        ILogger l,
        PrerequisiteAvailabilityPolicy availabilityPolicy)
        : this(
            () => VS.GetRequiredService<SComponentModel, IComponentModel>()
                .GetService<IVsFolderWorkspaceService>(),
            new TL
            {
                L = l,
            },
            availabilityPolicy,
            RustAnalyzerPackage.JTF)
    {
    }

    public TestContainerDiscoverer(
        Func<IVsFolderWorkspaceService> getWorkspaceFactory,
        TL tl,
        PrerequisiteAvailabilityPolicy availabilityPolicy,
        JoinableTaskFactory joinableTaskFactory)
        : this(
            getWorkspaceFactory,
            tl,
            null,
            availabilityPolicy,
            joinableTaskFactory)
    {
    }

    public TestContainerDiscoverer(
        Func<IVsFolderWorkspaceService> getWorkspaceFactory,
        TL tl,
        ILoggerFactory loggerFactory,
        PrerequisiteAvailabilityPolicy availabilityPolicy,
        JoinableTaskFactory joinableTaskFactory)
    {
        _getWorkspaceFactory = EnsureArg.IsNotNull(
            getWorkspaceFactory,
            nameof(getWorkspaceFactory),
            options => options.WithException(
                new ArgumentNullException(nameof(getWorkspaceFactory))));
        _tl = EnsureArg.IsNotNull(
            tl,
            nameof(tl),
            options => options.WithException(new ArgumentNullException(nameof(tl))));
        _availabilityPolicy = EnsureArg.IsNotNull(
            availabilityPolicy,
            nameof(availabilityPolicy),
            options => options.WithException(
                new ArgumentNullException(nameof(availabilityPolicy))));
        EnsureArg.IsNotNull(
            joinableTaskFactory,
            nameof(joinableTaskFactory),
            options => options.WithException(
                new ArgumentNullException(nameof(joinableTaskFactory))));
        if (loggerFactory == null)
        {
            _logger = LegacyLoggerBridge.ToMelLogger(_tl.L);
            _testContainerLogger = LegacyLoggerBridge.ToMelLogger(_tl.L);
        }
        else
        {
            _logger = loggerFactory.CreateLogger(
                typeof(TestContainerDiscoverer).FullName);
            _testContainerLogger = loggerFactory.CreateLogger(
                typeof(TestContainer).FullName);
        }

        _lifetimeToken = _lifetimeCancellation.Token;
        var initialization = joinableTaskFactory.RunAsync(InitializeAsync);
        Initialization = initialization.Task;
        ObserveInitialization();
    }

    public event EventHandler TestContainersUpdated;

    public Uri ExecutorUri => new(Constants.ExecutorUriString);

    public IEnumerable<ITestContainer> TestContainers
    {
        get
        {
            lock (_sync)
            {
                return !_disposed &&
                    _availabilityPolicy.IsReady(AutomaticRustPath.RustTestDiscoveryExecutionHandoff)
                        ? _testContainersCache.Values.ToArray()
                        : Enumerable.Empty<ITestContainer>();
            }
        }
    }

    public Task Initialization { get; }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_workspaceFactory != null)
            {
                _workspaceFactory.OnActiveWorkspaceChanged -= ActiveWorkspaceChangedEventHandlerAsync;
                _workspaceFactory = null;
            }

            UnloadOldWorkspaceUnderLock();
            _testContainersCache.Clear();
            TestContainersUpdated = null;
        }

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (!await _availabilityPolicy.WaitForReadyAsync(
                    AutomaticRustPath.RustTestDiscoveryExecutionHandoff,
                    _lifetimeToken))
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _workspaceFactory = _getWorkspaceFactory();
                if (_workspaceFactory == null)
                {
                    return;
                }

                _workspaceFactory.OnActiveWorkspaceChanged += ActiveWorkspaceChangedEventHandlerAsync;
            }

            await ActiveWorkspaceChangedEventHandlerAsync(this, new EventArgs());
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
        }
    }

    private async Task ActiveWorkspaceChangedEventHandlerAsync(object sender, EventArgs eventArgs)
    {
        try
        {
            await _workspaceChangeGate.WaitAsync(_lifetimeToken);
            try
            {
                if (!await _availabilityPolicy.IsReadyAsync(
                        AutomaticRustPath.RustTestDiscoveryExecutionHandoff,
                        _lifetimeToken))
                {
                    return;
                }

                _lifetimeToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _testContainersCache.Clear();
                    UnloadOldWorkspaceUnderLock();
                }

                await LoadNewWorkspaceAsync();
            }
            finally
            {
                _workspaceChangeGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            ReportUnexpectedFault(
                "TestContainerDiscoverer.ActiveWorkspaceChangedEventHandlerAsync",
                e);
        }
    }

    private async Task LoadNewWorkspaceAsync()
    {
        IWorkspace workspace;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_workspaceFactory == null)
            {
                return;
            }

            workspace = _workspaceFactory.CurrentWorkspace;
            _currentWorkspace = workspace;
        }

        if (workspace == null)
        {
            return;
        }

        _logger.LogInformation(
            new EventId(1, "WorkspaceLoading"),
            "TestContainerDiscoverer loading new workspace at '{WorkspaceLocation}'.",
            workspace.Location);
        var metadataService = workspace.GetService<IMetadataService>();
        if (metadataService == null)
        {
            return;
        }

        var packages = await metadataService.GetCachedPackagesAsync(_lifetimeToken);
        _lifetimeToken.ThrowIfCancellationRequested();
        if (packages == null)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _currentMetadataService = metadataService;
            metadataService.PackageAdded += PackageAddedEventHandler;
            metadataService.PackageRemoved += PackageRemovedEventHandler;
            metadataService.TestContainerUpdated += TestContainerUpdatedEventHandler;
            packages.ForEach(package => PackageAddedEventHandler(this, package));
        }
    }

    private void UnloadOldWorkspaceUnderLock()
    {
        _logger.LogInformation(
            new EventId(2, "WorkspaceUnloading"),
            "Unloading workspace at '{WorkspaceLocation}'.",
            _currentWorkspace?.Location);
        if (_currentMetadataService != null)
        {
            _currentMetadataService.TestContainerUpdated -= TestContainerUpdatedEventHandler;
            _currentMetadataService.PackageRemoved -= PackageRemovedEventHandler;
            _currentMetadataService.PackageAdded -= PackageAddedEventHandler;
            _currentMetadataService = null;
        }

        _currentWorkspace = null;
    }

    private void PackageAddedEventHandler(object sender, Workspace.Package e)
    {
        lock (_sync)
        {
            if (_disposed ||
                !_availabilityPolicy.IsReady(AutomaticRustPath.RustTestDiscoveryExecutionHandoff))
            {
                return;
            }

            _logger.LogInformation(
                new EventId(3, "PackageAdded"),
                "TCD: Package Added EventHandler: '{ManifestPath}'",
                e.ManifestPath);
            GetTestContainers(e).ForEach(c => TestContainerUpdatedEventHandler(this, c));
        }
    }

    private void PackageRemovedEventHandler(object sender, Workspace.Package e)
    {
        lock (_sync)
        {
            if (_disposed ||
                !_availabilityPolicy.IsReady(AutomaticRustPath.RustTestDiscoveryExecutionHandoff))
            {
                return;
            }

            _logger.LogInformation(
                new EventId(4, "PackageRemoved"),
                "TCD: Package Removed EventHandler: '{ManifestPath}'",
                e.ManifestPath);
            GetTestContainers(e).ForEach(c => TestContainerUpdatedEventHandler(this, c));
        }
    }

    private void TestContainerUpdatedEventHandler(object sender, PathEx e)
    {
        lock (_sync)
        {
            if (_disposed ||
                !_availabilityPolicy.IsReady(AutomaticRustPath.RustTestDiscoveryExecutionHandoff))
            {
                return;
            }

            _logger.LogInformation(
                new EventId(5, "TestContainerUpdated"),
                "TCD: TestContainer Updated EventHandler: '{TestContainerPath}'",
                e);
            if (e.FileExists())
            {
                TryAddTestContainer(e);
            }
            else
            {
                TryRemoveTestContainer(e);
            }

            TestContainersUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TryAddTestContainer(PathEx container)
    {
        if (!_testContainersCache.TryAdd(
                container,
                new TestContainer(
                    container,
                    this,
                    _tl,
                    _testContainerLogger)))
        {
            _logger.LogError(
                new EventId(6, "TestContainerAddFailed"),
                "TCD: Failed to add '{TestContainerPath}'",
                container);
        }
    }

    private void TryRemoveTestContainer(PathEx container)
    {
        if (!_testContainersCache.TryRemove(container, out _))
        {
            _logger.LogError(
                new EventId(7, "TestContainerRemoveFailed"),
                "TCD: Failed to remove container {TestContainerPath}.",
                container);
        }
    }

    private IEnumerable<PathEx> GetTestContainers(Workspace.Package e)
    {
        var profile = _currentWorkspace?.GetProfile(e.ManifestPath) ??
            e.GetProfiles().FirstOrDefault();
        return profile == null
            ? Enumerable.Empty<PathEx>()
            : e.GetTestContainers(profile).Select(x => x.Container);
    }

    private void ObserveInitialization()
    {
        var observation = Initialization.ContinueWith(
            task => ReportUnexpectedFault(
                "TestContainerDiscoverer.InitializeAsync",
                task.Exception.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        KS.RustAnalyzer.TestAdapter.Common.TaskExtensions.Forget(observation);
    }

    private void ReportUnexpectedFault(string operation, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        _logger.LogError(
            new EventId(8, "BackgroundOperationFailed"),
            exception,
            "Operation '{Operation}' failed unexpectedly.",
            operation);
    }
}
