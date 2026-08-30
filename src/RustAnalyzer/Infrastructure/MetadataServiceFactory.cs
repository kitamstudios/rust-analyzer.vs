using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.Infrastructure;

[ExportWorkspaceServiceFactory(WorkspaceServiceFactoryOptions.None, typeof(IMetadataService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetadataServiceFactory : IWorkspaceServiceFactory
{
    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public Lazy<IToolchainService> CargoService { get; set; }

    [Import]
    public PrerequisiteAvailabilityPolicy AvailabilityPolicy { get; set; }

    public object CreateService(IWorkspace workspaceContext)
    {
        return CreateService(
            workspaceContext,
            () => workspaceContext.GetFileWatcherService(),
            RustAnalyzerPackage.JTF);
    }

    public object CreateService(
        IWorkspace workspaceContext,
        Func<IFileWatcherService> getFileWatcherService,
        JoinableTaskFactory joinableTaskFactory)
    {
        EnsureArg.IsNotNull(workspaceContext);
        EnsureArg.IsNotNull(getFileWatcherService);
        EnsureArg.IsNotNull(joinableTaskFactory);
        return new PrerequisiteGatedMetadataService(
            workspaceContext,
            getFileWatcherService,
            CargoService,
            new TL { T = T, L = L, },
            AvailabilityPolicy,
            joinableTaskFactory);
    }

    private sealed class PrerequisiteGatedMetadataService : IMetadataService, IDisposable
    {
        private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;
        private readonly Lazy<IToolchainService> _cargoService;
        private readonly Func<IFileWatcherService> _getFileWatcherService;
        private readonly JoinableTask _initialization;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly CancellationToken _lifetimeToken;
        private readonly object _sync = new();
        private readonly TL _tl;
        private readonly MetadataWorkspaceUpdateHandler _updateHandler;
        private readonly IWorkspace _workspace;
        private int _activeOperations;
        private IFileWatcherService _fileWatcherService;
        private MetadataService _metadataService;
        private bool _stopping;

        public PrerequisiteGatedMetadataService(
            IWorkspace workspace,
            Func<IFileWatcherService> getFileWatcherService,
            Lazy<IToolchainService> cargoService,
            TL tl,
            PrerequisiteAvailabilityPolicy availabilityPolicy,
            JoinableTaskFactory joinableTaskFactory)
        {
            _workspace = workspace;
            _getFileWatcherService = getFileWatcherService;
            _cargoService = cargoService;
            _availabilityPolicy = availabilityPolicy;
            _lifetimeToken = _lifetimeCancellation.Token;
            _tl = tl;
            _updateHandler = new MetadataWorkspaceUpdateHandler(availabilityPolicy);
            _initialization = joinableTaskFactory.RunAsync(InitializeAsync);
            ObserveInitialization();
        }

        public event EventHandler<Workspace.Package> PackageAdded;

        public event EventHandler<Workspace.Package> PackageRemoved;

        public event EventHandler<PathEx> TestContainerUpdated;

        public Task<Workspace.Package> GetPackageAsync(
            PathEx manifestPath,
            CancellationToken cancellationToken)
        {
            return RunAsync(
                (service, token) => service.GetPackageAsync(manifestPath, token),
                cancellationToken);
        }

        public Task<Workspace.Package> GetContainingPackageAsync(
            PathEx filePath,
            CancellationToken cancellationToken)
        {
            return RunAsync(
                (service, token) => service.GetContainingPackageAsync(filePath, token),
                cancellationToken);
        }

        public Task<int> OnWorkspaceUpdateAsync(
            IEnumerable<PathEx> filePaths,
            CancellationToken cancellationToken)
        {
            return RunAsync(
                (service, token) => service.OnWorkspaceUpdateAsync(filePaths, token),
                cancellationToken);
        }

        public Task<IEnumerable<Workspace.Package>> GetCachedPackagesAsync(
            CancellationToken cancellationToken)
        {
            return RunAsync(
                (service, token) => service.GetCachedPackagesAsync(token),
                cancellationToken);
        }

        public void Dispose()
        {
            MetadataService metadataService;
            lock (_sync)
            {
                if (_stopping)
                {
                    return;
                }

                _stopping = true;
                if (_fileWatcherService != null)
                {
                    _fileWatcherService.OnBatchFileSystemChanged -= OnBatchFileSystemChangedAsync;
                    _fileWatcherService = null;
                }

                if (_metadataService != null)
                {
                    _metadataService.PackageAdded -= OnPackageAdded;
                    _metadataService.PackageRemoved -= OnPackageRemoved;
                    _metadataService.TestContainerUpdated -= OnTestContainerUpdated;
                }

                PackageAdded = null;
                PackageRemoved = null;
                TestContainerUpdated = null;
                metadataService = TakeMetadataServiceForDisposalUnderLock();
            }

            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            metadataService?.Dispose();
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (!await _availabilityPolicy.WaitForReadyAsync(
                        AutomaticRustPath.WorkspaceMetadata,
                        _lifetimeToken))
                {
                    return;
                }

                lock (_sync)
                {
                    if (_stopping)
                    {
                        return;
                    }

                    MetadataService metadataService = null;
                    try
                    {
                        metadataService = new MetadataService(
                            _cargoService.Value,
                            (PathEx)_workspace.Location,
                            _tl);
                        var fileWatcherService = _getFileWatcherService();

                        metadataService.PackageAdded += OnPackageAdded;
                        metadataService.PackageRemoved += OnPackageRemoved;
                        metadataService.TestContainerUpdated += OnTestContainerUpdated;
                        fileWatcherService.OnBatchFileSystemChanged += OnBatchFileSystemChangedAsync;

                        _metadataService = metadataService;
                        _fileWatcherService = fileWatcherService;
                    }
                    catch
                    {
                        if (metadataService != null)
                        {
                            metadataService.PackageAdded -= OnPackageAdded;
                            metadataService.PackageRemoved -= OnPackageRemoved;
                            metadataService.TestContainerUpdated -= OnTestContainerUpdated;
                            metadataService.Dispose();
                        }

                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
            }
        }

        private async Task<T> RunAsync<T>(
            Func<MetadataService, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
            var metadataService = await EnterOperationAsync(operationCancellation.Token);
            try
            {
                return await operation(metadataService, operationCancellation.Token);
            }
            finally
            {
                ExitOperation();
            }
        }

        private async Task<MetadataService> EnterOperationAsync(CancellationToken cancellationToken)
        {
            await _initialization.JoinAsync(cancellationToken);
            lock (_sync)
            {
                if (_stopping)
                {
                    throw new ObjectDisposedException(nameof(PrerequisiteGatedMetadataService));
                }

                if (_metadataService == null)
                {
                    throw new InvalidOperationException(
                        "Workspace metadata is unavailable because prerequisites are not ready.");
                }

                _activeOperations++;
                return _metadataService;
            }
        }

        private bool TryEnterOperation(out MetadataService metadataService)
        {
            lock (_sync)
            {
                if (_stopping || _metadataService == null)
                {
                    metadataService = null;
                    return false;
                }

                _activeOperations++;
                metadataService = _metadataService;
                return true;
            }
        }

        private void ExitOperation()
        {
            MetadataService metadataService;
            lock (_sync)
            {
                _activeOperations--;
                metadataService = TakeMetadataServiceForDisposalUnderLock();
            }

            metadataService?.Dispose();
        }

        private MetadataService TakeMetadataServiceForDisposalUnderLock()
        {
            if (!_stopping ||
                _activeOperations != 0 ||
                _metadataService == null)
            {
                return null;
            }

            var metadataService = _metadataService;
            _metadataService = null;
            return metadataService;
        }

        private void ObserveInitialization()
        {
            var observation = _initialization.Task.ContinueWith(
                task => ReportUnexpectedFault(
                    "MetadataServiceFactory.PrerequisiteGatedMetadataService.InitializeAsync",
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
                if (_stopping)
                {
                    return;
                }
            }

            _tl.L.WriteError(
                "Operation '{0}' failed unexpectedly. Ex: {1}",
                operation,
                exception);
        }

        private async Task OnBatchFileSystemChangedAsync(
            object sender,
            BatchFileSystemEventArgs eventArgs)
        {
            if (!TryEnterOperation(out var metadataService))
            {
                return;
            }

            try
            {
                try
                {
                    await _updateHandler.HandleAsync(
                        eventArgs.FileSystemEvents
                            .Select(fileSystemEvent => (PathEx?)fileSystemEvent.FullPath)
                            .Where(path => path.HasValue)
                            .Select(path => path.Value),
                        metadataService,
                        _lifetimeToken);
                }
                catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
                {
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        private void OnPackageAdded(object sender, Workspace.Package package)
        {
            lock (_sync)
            {
                if (!_stopping)
                {
                    PackageAdded?.Invoke(this, package);
                }
            }
        }

        private void OnPackageRemoved(object sender, Workspace.Package package)
        {
            lock (_sync)
            {
                if (!_stopping)
                {
                    PackageRemoved?.Invoke(this, package);
                }
            }
        }

        private void OnTestContainerUpdated(object sender, PathEx testContainer)
        {
            lock (_sync)
            {
                if (!_stopping)
                {
                    TestContainerUpdated?.Invoke(this, testContainer);
                }
            }
        }
    }
}

public sealed class MetadataWorkspaceUpdateHandler
{
    private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;

    public MetadataWorkspaceUpdateHandler(PrerequisiteAvailabilityPolicy availabilityPolicy)
    {
        _availabilityPolicy = EnsureArg.IsNotNull(
            availabilityPolicy,
            nameof(availabilityPolicy),
            options => options.WithException(
                new ArgumentNullException(nameof(availabilityPolicy))));
    }

    public async Task HandleAsync(
        IEnumerable<PathEx> filePaths,
        IMetadataService metadataService,
        CancellationToken cancellationToken)
    {
        if (!await _availabilityPolicy.IsReadyAsync(
                AutomaticRustPath.WorkspaceMetadata,
                cancellationToken))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var relevantFilePaths = filePaths
            .Where(x => x.IsTestContainer() || x.IsManifest() || x.IsRustFile())
            .Distinct()
            .ToArray();
        if (relevantFilePaths.Length == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await metadataService.OnWorkspaceUpdateAsync(relevantFilePaths, cancellationToken);
    }
}
