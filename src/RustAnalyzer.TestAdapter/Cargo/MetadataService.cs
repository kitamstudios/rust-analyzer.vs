using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public class MetadataService : IMetadataService, IDisposable
{
    private readonly IToolchainService _cargoService;
    private readonly PathEx _workspaceRoot;
    private readonly MelLogger _logger;
    private readonly bool _synchronousEvents;
    private readonly SemaphoreSlim _packageCacheLocker = new(1, 1);
    private ConcurrentDictionary<PathEx, Workspace.Package> _packageCache = new();
    private bool _disposedValue;

    public MetadataService(IToolchainService cargoService, PathEx workspaceRoot, MelLogger logger)
        : this(cargoService, workspaceRoot, logger, syncEvents: false)
    {
    }

    protected MetadataService(IToolchainService cargoService, PathEx workspaceRoot, MelLogger logger, bool syncEvents = false)
    {
        _cargoService = cargoService;
        _workspaceRoot = workspaceRoot;
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        _synchronousEvents = syncEvents;
        _logger.LogInformation(
            new EventId(1, "MetadataServiceCreated"),
            "Creating MDS. Workspace root: {WorkspaceRoot}.",
            workspaceRoot);
    }

    public event EventHandler<Workspace.Package> PackageAdded;

    public event EventHandler<Workspace.Package> PackageRemoved;

    public event EventHandler<PathEx> TestContainerUpdated;

    public Action DisconnectEvents { get; set; } = () => { };

    public void Dispose()
    {
        // NOTE: Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method.
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Task<Workspace.Package> GetPackageAsync(PathEx manifestPath, CancellationToken ct)
    {
        _logger.LogInformation(
            new EventId(2, "PackageRequested"),
            "GetPackageAsync. Manifest path: {ManifestPath}.",
            manifestPath);
        return ProtectPackageCacheAndRunAsync((ct) => GetCachedPackageAsync(manifestPath, ct), ct);
    }

    public async Task<Workspace.Package> GetContainingPackageAsync(PathEx filePath, CancellationToken ct)
    {
        _logger.LogInformation(
            new EventId(3, "ContainingPackageRequested"),
            "GetContainingPackageAsync. File path: {FilePath}.",
            filePath);
        if (!filePath.TryGetParentManifestOrThisUnderWorkspace(_workspaceRoot, out PathEx? manifest))
        {
            _logger.LogInformation(
                new EventId(4, "ContainingPackageNotFound"),
                "GetContainingPackageAsync. No containing package found.");
            return null;
        }

        Ensure.That(manifest).IsNotNull();
        return await GetPackageAsync(manifest.Value, ct);
    }

    public async Task<int> OnWorkspaceUpdateAsync(IEnumerable<PathEx> filePaths, CancellationToken ct)
    {
        foreach (var filePath in filePaths.Where(fp => fp.IsTestContainer()))
        {
            OnTestContainerUpdated(filePath);
        }

        await ProtectPackageCacheAndRunAsync(
            (ct) =>
            {
                foreach (var filePath in filePaths.Where(fp => fp.IsManifest() || fp.IsRustFile()))
                {
                    if (filePath.TryGetParentManifestOrThisUnderWorkspace(_workspaceRoot, out PathEx? manifest))
                    {
                        _logger.LogInformation(
                            new EventId(5, "PackageCacheEntryRemoved"),
                            "OnWorkspaceUpdateAsync: Removing from cache: {ManifestPath}",
                            manifest);
                        if (_packageCache.TryRemove(manifest.Value, out var package))
                        {
                            OnPackageRemoved(package);
                        }
                    }
                }

                return 0.ToTask();
            },
            ct);

        return filePaths.Count();
    }

    public Task<IEnumerable<Workspace.Package>> GetCachedPackagesAsync(CancellationToken ct)
    {
        return ProtectPackageCacheAndRunAsync(
            (ct) =>
            {
                var packages = new List<Workspace.Package>(_packageCache.Values) as IEnumerable<Workspace.Package>;
                return packages.ToTask();
            },
            ct);
    }

    private async Task<T> ProtectPackageCacheAndRunAsync<T>(Func<CancellationToken, Task<T>> f, CancellationToken ct)
    {
        await _packageCacheLocker.WaitAsync(ct);
        try
        {
            return await f(ct);
        }
        finally
        {
            _packageCacheLocker.Release();
        }
    }

    private async Task<Workspace.Package> GetCachedPackageAsync(PathEx manifestPath, CancellationToken ct)
    {
        if (_packageCache.TryGetValue(manifestPath, out var package))
        {
            return package;
        }

        _logger.LogInformation(
            new EventId(6, "PackageCacheMiss"),
            "... Cache miss: {ManifestPath}.",
            manifestPath);
        package = await GetPackageAsyncCore(manifestPath, ct);
        _packageCache[manifestPath] = package;
        OnPackageAdded(package);
        return package;
    }

    private async Task<Workspace.Package> GetPackageAsyncCore(PathEx manifestPath, CancellationToken ct)
    {
        var w = await _cargoService.GetWorkspaceAsync(manifestPath, ct);
        var p = w.Packages.FirstOrDefault(p => p.ManifestPath.GetFullPath() == manifestPath.GetFullPath());

        Ensure.That(p).IsNotNull();
        return p;
    }

    private void Dispose(bool disposing)
    {
        _logger.LogInformation(
            new EventId(7, "MetadataServiceDisposing"),
            "Disposing MDS. Package cache has {PackageCount} entries.",
            _packageCache.Count);
        if (!_disposedValue)
        {
            if (disposing)
            {
                // NOTE: Dispose managed state (managed objects).
            }

            DisconnectEvents();
            PackageRemoved = null;
            PackageAdded = null;
            _packageCache = null;
            _disposedValue = true;
        }
    }

    // NOTE: This fire-n-forget ensure we dont hold the lock for longer than necessary.
    private void OnPackageAdded(Workspace.Package package)
    {
        var t = Task.Run(
            async () =>
            {
                PackageAdded?.Invoke(this, package);
                await Task.CompletedTask;
            });

        if (_synchronousEvents)
        {
            // NOTE: Testability hook.
            t.Wait();
        }
        else
        {
            ObserveEventDispatch(
                t,
                "MetadataService.PackageAddedDispatch");
        }
    }

    // NOTE: This fire-n-forget ensure we dont hold the lock for longer than necessary.
    private void OnPackageRemoved(Workspace.Package package)
    {
        var t = Task.Run(
            async () =>
            {
                PackageRemoved?.Invoke(this, package);
                await Task.CompletedTask;
            });

        if (_synchronousEvents)
        {
            // NOTE: Testability hook.
            t.Wait();
        }
        else
        {
            ObserveEventDispatch(
                t,
                "MetadataService.PackageRemovedDispatch");
        }
    }

    // NOTE: This fire-n-forget ensure we dont hold the lock for longer than necessary.
    private void OnTestContainerUpdated(PathEx testContainer)
    {
        var t = Task.Run(
            async () =>
            {
                TestContainerUpdated?.Invoke(this, testContainer);
                await Task.CompletedTask;
            });

        if (_synchronousEvents)
        {
            // NOTE: Testability hook.
            t.Wait();
        }
        else
        {
            ObserveEventDispatch(
                t,
                "MetadataService.TestContainerUpdatedDispatch");
        }
    }

    private void ObserveEventDispatch(Task operation, string operationName)
    {
        operation.ContinueWith(
                task => ReportUnexpectedFault(
                    operationName,
                    task.Exception.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default)
            .Forget();
    }

    private void ReportUnexpectedFault(string operation, Exception exception)
    {
        if (_disposedValue || exception is OperationCanceledException)
        {
            return;
        }

        _logger.LogError(
            new EventId(8, "EventDispatchFailed"),
            exception,
            "Operation '{Operation}' failed unexpectedly.",
            operation);
    }
}
