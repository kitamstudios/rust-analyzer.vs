using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public class MetadataService : IMetadataService, IDisposable
{
    private readonly IToolChainService _cargoService;
    private readonly PathEx _workspaceRoot;
    private readonly TL _tl;
    private readonly bool _synchronousEvents;
    private readonly SemaphoreSlim _packageCacheLocker = new(1, 1);
    private ConcurrentDictionary<PathEx, Workspace.Package> _packageCache = new();
    private bool _disposedValue;

    public MetadataService(IToolChainService cargoService, PathEx workspaceRoot, TL tl)
        : this(cargoService, workspaceRoot, tl, syncEvents: false)
    {
    }

    protected MetadataService(IToolChainService cargoService, PathEx workspaceRoot, TL tl, bool syncEvents = false)
    {
        _cargoService = cargoService;
        _workspaceRoot = workspaceRoot;
        _tl = tl;
        _synchronousEvents = syncEvents;
        _tl.L.WriteLine("Creating MDS. Workspace root: {0}.", workspaceRoot);
        _tl.T.TrackEvent("CreatingMDS", ("WorkspaceRoot", $"{workspaceRoot}"));
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
        _tl.L.WriteLine("GetPackageAsync. Manifest path: {0}.", manifestPath);
        return ProtectPackageCacheAndRunAsync((ct) => GetCachedPackageAsync(manifestPath, ct), ct);
    }

    public async Task<Workspace.Package> GetContainingPackageAsync(PathEx filePath, CancellationToken ct)
    {
        _tl.L.WriteLine("GetContainingPackageAsync. File path: {0}.", filePath);
        if (!filePath.TryGetParentManifestOrThisUnderWorkspace(_workspaceRoot, out PathEx? manifest))
        {
            _tl.L.WriteLine("GetContainingPackageAsync. No containing package found.");
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
                        _tl.L.WriteLine("OnWorkspaceUpdateAsync: Removing from cache: {0}", manifest);
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

        _tl.L.WriteLine("... Cache miss: {0}.", manifestPath);
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
        _tl.L.WriteLine("Disposing MDS. Package cache has {0} entries.", _packageCache.Count);
        _tl.T.TrackEvent("DisposeMDS", ("PackageCount", $"{_packageCache.Count}"));
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
            t.Forget();
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
            t.Forget();
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
            t.Forget();
        }
    }
}
