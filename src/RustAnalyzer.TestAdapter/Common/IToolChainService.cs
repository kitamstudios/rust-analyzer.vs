using System;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IToolChainService
{
    PathEx? GetRustUpExePath();

    PathEx? GetCargoExePath();

    Task<PathEx> GetRustAnalyzerExePath();

    Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> CleanAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<Workspace> GetWorkspaceAsync(PathEx manifestPath, CancellationToken ct);
}

public sealed class BuildTargetInfo
{
    public PathEx WorkspaceRoot { get; set; }

    public PathEx FilePath { get; set; }

    public string Profile { get; set; }

    public string AdditionalBuildArgs { get; set; } = string.Empty;
}

public sealed class BuildOutputSinks
{
    public Func<BuildMessage, Task> BuildActionProgressReporter { get; set; }

    public IBuildOutputSink OutputSink { get; set; }
}