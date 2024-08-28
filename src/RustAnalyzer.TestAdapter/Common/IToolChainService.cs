using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IToolchainService
{
    PathEx GetCargoExePath();

    Task<bool> BuildAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> CleanAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> RunClippyAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<bool> RunFmtAsync(BuildTargetInfo bti, BuildOutputSinks bos, CancellationToken ct);

    Task<Workspace> GetWorkspaceAsync(PathEx manifestPath, CancellationToken ct);

    /// <summary>
    /// NOTE: This is shameful. Cannot pull in IAsyncEnumerable without getting into dependency hell.
    /// </summary>
    Task<IEnumerable<Task<TestSuiteInfo>>> GetTestSuiteInfoAsync(PathEx testContainerPath, string profile, CancellationToken ct);
}

public sealed class TestContainer
{
    public PathEx ThisPath { get; set; }

    public PathEx Manifest { get; set; }

    public PathEx TargetDir { get; set; }

    public string AdditionalTestDiscoveryArguments { get; set; }

    public string AdditionalTestExecutionArguments { get; set; }

    public string TestExecutionEnvironment { get; set; }

    public string Profile { get; set; }

    public PathEx[] TestExes { get; set; }
}

public sealed class BuildTargetInfo
{
    public PathEx WorkspaceRoot { get; set; }

    public PathEx ManifestPath { get; set; }

    public string Profile { get; set; }

    public string AdditionalBuildArgs { get; set; } = string.Empty;

    public string AdditionalTestDiscoveryArguments { get; set; } = string.Empty;

    public string AdditionalTestExecutionArguments { get; set; } = string.Empty;

    public string TestExecutionEnvironment { get; set; } = string.Empty;
}

public sealed class BuildOutputSinks
{
    public Func<BuildMessage, Task> BuildActionProgressReporter { get; set; }

    public IBuildOutputSink OutputSink { get; set; }
}
