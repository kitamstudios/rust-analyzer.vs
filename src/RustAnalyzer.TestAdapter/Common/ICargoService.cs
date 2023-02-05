using System;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface ICargoService
{
    string GetCargoExePath();

    Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> CleanAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<Metadata> GetMetadata(PathEx workspaceRoot, CancellationToken ct);
}

public sealed class BuildTargetInfo
{
    public string WorkspaceRoot { get; set; }

    public string FilePath { get; set; }

    public string Profile { get; set; }

    public string AdditionalBuildArgs { get; set; } = string.Empty;
}

public sealed class BuildOutputSinks
{
    public Func<BuildMessage, Task> BuildActionProgressReporter { get; set; }

    public Func<string, Task> ShowMessageBox { get; set; }

    public IBuildOutputSink OutputSink { get; set; }
}
