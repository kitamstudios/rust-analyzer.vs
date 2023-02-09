using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IMetadataService
{
    Task<Workspace.Package> GetPackageAsync(PathEx manifestPath, CancellationToken ct);

    Task<Workspace.Package> GetContainingPackageAsync(PathEx filePath, CancellationToken ct);

    Task<int> OnWorkspaceUpdateAsync(IEnumerable<PathEx> filePaths, CancellationToken ct);
}
