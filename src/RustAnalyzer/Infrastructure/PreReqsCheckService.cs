using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

public interface IPreReqsCheckService
{
    Task<PrerequisiteResult> EvaluateAsync(CancellationToken ct);
}

[Export(typeof(IPreReqsCheckService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PreReqsCheckService : IPreReqsCheckService
{
    private readonly PrerequisiteEvaluator _evaluator;
    private readonly TL _tl;

    [ImportingConstructor]
    public PreReqsCheckService([Import] ITelemetryService t, [Import] ILogger l)
        : this(new VisualStudioPrerequisiteProbe(), t, l)
    {
    }

    public PreReqsCheckService(IPrerequisiteProbe probe, ITelemetryService t, ILogger l)
    {
        _evaluator = new PrerequisiteEvaluator(probe);
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public async Task<PrerequisiteResult> EvaluateAsync(CancellationToken ct)
    {
        try
        {
            return await _evaluator.EvaluateAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _tl.L.WriteError("Prerequisite evaluation failed unexpectedly. Ex: {0}", e);
            _tl.T.TrackException(e);
            return PrerequisiteResult.Failed(
                new[]
                {
                    new PrerequisiteFailure(
                        PrerequisiteFailureKind.PrerequisiteEvaluationFailed,
                        "Prerequisite evaluation could not complete. Review Output > rust-analyzer.vs for diagnostics, repair the reported environment issue, then restart Visual Studio."),
                });
        }
    }
}
