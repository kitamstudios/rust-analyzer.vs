using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Threading;

namespace KS.RustAnalyzer.Infrastructure;

public enum AutomaticRustPath
{
    PackageFollowOnStartup,
    LanguageClientActivation,
    WorkspaceMetadata,
    WorkspaceFileScanning,
    OpenFolderContextDiscovery,
    OpenFolderBuild,
    OpenFolderClean,
    NodeBrowseObject,
    RustTestDiscoveryExecutionHandoff,
    DebugRunPreparation,
    RustAnalyzerUpdaterDownload,
    ToolchainStatusQuery,
}

[Export(typeof(PrerequisiteAvailabilityPolicy))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PrerequisiteAvailabilityPolicy
{
    private static readonly string[] PathNames =
    {
        "package follow-on startup",
        "language-client activation",
        "workspace metadata",
        "workspace file scanning",
        "Open Folder context discovery",
        "Open Folder build",
        "Open Folder clean",
        "Rust node properties",
        "Rust test discovery/execution handoff",
        "debug/run preparation",
        "rust-analyzer updater/download",
        "toolchain status query",
    };

    private readonly ILogger _logger;
    private readonly int[] _reportedSuppressions = new int[PathNames.Length];
    private readonly PrerequisiteProcessState _state;
    private readonly ITelemetryService _telemetry;
    private int _infoBarFailureReported;
    private int _suspensionReported;

    [ImportingConstructor]
    public PrerequisiteAvailabilityPolicy([Import] ILogger logger, [Import] ITelemetryService telemetry)
        : this(PrerequisiteProcessState.Current, logger, telemetry)
    {
    }

    public PrerequisiteAvailabilityPolicy(
        PrerequisiteProcessState state,
        ILogger logger,
        ITelemetryService telemetry)
    {
        _state = EnsureArg.IsNotNull(
            state,
            nameof(state),
            options => options.WithException(new ArgumentNullException(nameof(state))));
        _logger = EnsureArg.IsNotNull(
            logger,
            nameof(logger),
            options => options.WithException(new ArgumentNullException(nameof(logger))));
        _telemetry = EnsureArg.IsNotNull(
            telemetry,
            nameof(telemetry),
            options => options.WithException(new ArgumentNullException(nameof(telemetry))));
    }

    public bool IsReady(AutomaticRustPath path)
    {
        var pathIndex = GetPathIndex(path);
        var status = _state.Status;
        if (status == PrerequisiteStatus.Ready)
        {
            return true;
        }

        if (Interlocked.CompareExchange(ref _reportedSuppressions[pathIndex], 1, 0) == 0)
        {
            _logger.WriteLine(
                "Suppressed automatic Rust path '{0}': prerequisite state is {1} for this Visual Studio session. Restart Visual Studio to recheck prerequisites.",
                PathNames[pathIndex],
                status);
        }

        return false;
    }

    public async Task<bool> IsReadyAsync(AutomaticRustPath path, CancellationToken cancellationToken)
    {
        GetPathIndex(path);
        cancellationToken.ThrowIfCancellationRequested();
        var status = _state.GetEvaluationStatus(out var completion);
        if (status == PrerequisiteStatus.Evaluating && completion != null)
        {
            await ThreadingTools.WithCancellation(completion, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return IsReady(path);
    }

    public async Task<bool> WaitForReadyAsync(
        AutomaticRustPath path,
        CancellationToken cancellationToken)
    {
        GetPathIndex(path);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _state.Status;
            if (status == PrerequisiteStatus.Ready)
            {
                return true;
            }

            if (status == PrerequisiteStatus.Failed ||
                status == PrerequisiteStatus.Suspended)
            {
                return IsReady(path);
            }

            await _state.WaitForStatusChangeAsync(status, cancellationToken);
        }
    }

    public void ReportSuspended()
    {
        if (_state.Status == PrerequisiteStatus.Suspended &&
            Interlocked.CompareExchange(ref _suspensionReported, 1, 0) == 0)
        {
            _logger.WriteLine(
                "rust-analyzer.vs entered prerequisite state Suspended for this Visual Studio session. Automatic Rust work is disabled. Restart Visual Studio to recheck prerequisites.");
        }
    }

    public void ReportInfoBarFailure(Exception exception)
    {
        EnsureArg.IsNotNull(
            exception,
            nameof(exception),
            options => options.WithException(new ArgumentNullException(nameof(exception))));

        if (Interlocked.CompareExchange(ref _infoBarFailureReported, 1, 0) == 0)
        {
            _logger.WriteError("Failed to show prerequisite suspension InfoBar. Ex: {0}", exception);
            _telemetry.TrackException(exception);
        }
    }

    private static int GetPathIndex(AutomaticRustPath path)
    {
        var pathIndex = (int)path;
        EnsureArg.IsTrue(
            pathIndex >= 0 &&
                pathIndex < PathNames.Length &&
                Enum.IsDefined(typeof(AutomaticRustPath), path),
            nameof(path),
            options => options.WithException(new ArgumentOutOfRangeException(nameof(path))));

        return pathIndex;
    }
}
