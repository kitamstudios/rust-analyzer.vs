using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IMetadataService
{
    event EventHandler<Workspace.Package> PackageAdded;

    event EventHandler<Workspace.Package> PackageRemoved;

    event EventHandler<PathEx> TestContainerUpdated;

    Task<Workspace.Package> GetPackageAsync(PathEx manifestPath, CancellationToken ct);

    Task<Workspace.Package> GetContainingPackageAsync(PathEx filePath, CancellationToken ct);

    Task<int> OnWorkspaceUpdateAsync(IEnumerable<PathEx> filePaths, CancellationToken ct);

    Task<IEnumerable<Workspace.Package>> GetCachedPackagesAsync(CancellationToken ct);
}
