using System;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IToolChainService
{
    PathEx? GetCargoExePath();

    Task<PathEx> GetRustAnalyzerExePath();

    Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> CleanAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> RunClippyAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> RunFmtAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<Workspace> GetWorkspaceAsync(PathEx manifestPath, CancellationToken ct);

    Task<TestSuiteInfo> GetTestSuiteInfoAsync(PathEx testContainerPath, string profile, CancellationToken ct);
}

public sealed class TestContainer
{
    public static readonly PathEx NotYetGeneratedMarker = (PathEx)"<not_yet_generated>";

    public PathEx Manifest { get; set; }

    public PathEx TargetDir { get; set; }

    public PathEx Source { get; set; }

    public PathEx TestExe { get; set; }
}

public sealed class BuildTargetInfo
{
    public PathEx WorkspaceRoot { get; set; }

    public PathEx ManifestPath { get; set; }

    public string Profile { get; set; }

    public string AdditionalBuildArgs { get; set; } = string.Empty;
}

public sealed class BuildOutputSinks
{
    public Func<BuildMessage, Task> BuildActionProgressReporter { get; set; }

    public IBuildOutputSink OutputSink { get; set; }
}
