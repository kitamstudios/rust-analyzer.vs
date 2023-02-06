using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IMetadataService
{
    Task<Workspace.Package> GetPackageAsync(PathEx manifestPath, CancellationToken ct);
}
