using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

namespace KS.RustAnalyzer.Infrastructure;

public enum PrerequisiteStatus
{
    NotEvaluated,
    Evaluating,
    Ready,
    Failed,
    Suspended,
}

public sealed class PrerequisiteFailure
{
    public PrerequisiteFailure(string check, string message)
    {
        Check = check ?? throw new ArgumentNullException(nameof(check));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public string Check { get; }

    public string Message { get; }
}

public sealed class PrerequisiteResult
{
    private PrerequisiteResult(ImmutableArray<PrerequisiteFailure> failures)
    {
        Failures = failures;
    }

    public static PrerequisiteResult Success { get; } = new(ImmutableArray<PrerequisiteFailure>.Empty);

    public bool IsSuccess => Failures.IsEmpty;

    public ImmutableArray<PrerequisiteFailure> Failures { get; }

    public static PrerequisiteResult Failed(IEnumerable<PrerequisiteFailure> failures)
    {
        if (failures == null)
        {
            throw new ArgumentNullException(nameof(failures));
        }

        var immutableFailures = ImmutableArray.CreateRange(failures);
        if (immutableFailures.IsEmpty)
        {
            throw new ArgumentException("A failed result requires at least one failure.", nameof(failures));
        }

        foreach (var failure in immutableFailures)
        {
            if (failure == null)
            {
                throw new ArgumentException("Failures cannot contain null.", nameof(failures));
            }
        }

        return new PrerequisiteResult(immutableFailures);
    }
}

public sealed class PrerequisiteProcessState
{
    private readonly object _sync = new();
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private Evaluation _evaluation;
    private PrerequisiteResult _result;
    private PrerequisiteStatus _status;

    public PrerequisiteProcessState(JoinableTaskFactory joinableTaskFactory)
    {
        _joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
    }

    public static PrerequisiteProcessState Current => ProcessStateHolder.Instance;

    public PrerequisiteStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return _status == PrerequisiteStatus.Ready;
            }
        }
    }

    public PrerequisiteResult CachedResult
    {
        get
        {
            lock (_sync)
            {
                return _result;
            }
        }
    }

    public Task<PrerequisiteResult> GetOrEvaluateAsync(
        Func<CancellationToken, Task<PrerequisiteResult>> evaluator,
        CancellationToken cancellationToken)
    {
        if (evaluator == null)
        {
            throw new ArgumentNullException(nameof(evaluator));
        }

        Evaluation evaluation;
        lock (_sync)
        {
            if (_evaluation == null)
            {
                _status = PrerequisiteStatus.Evaluating;
                _evaluation = new Evaluation(this, evaluator, cancellationToken, _joinableTaskFactory);
            }

            evaluation = _evaluation;
        }

        return evaluation.Task.GetValueAsync();
    }

    public void Suspend()
    {
        lock (_sync)
        {
            if (_status != PrerequisiteStatus.Failed)
            {
                throw new InvalidOperationException("Prerequisites can be suspended only after a failed evaluation.");
            }

            _status = PrerequisiteStatus.Suspended;
        }
    }

    private async Task<PrerequisiteResult> EvaluateAndCacheAsync(
        Evaluation evaluation,
        Func<CancellationToken, Task<PrerequisiteResult>> evaluator,
        CancellationToken cancellationToken)
    {
        var completed = false;
        try
        {
            var evaluationTask = _joinableTaskFactory.RunAsync(() => evaluator(cancellationToken));
            var result = await evaluationTask;
            if (result == null)
            {
                throw new InvalidOperationException("The prerequisite evaluator returned no result.");
            }

            lock (_sync)
            {
                _result = result;
                _status = result.IsSuccess ? PrerequisiteStatus.Ready : PrerequisiteStatus.Failed;
                completed = true;
            }

            return result;
        }
        finally
        {
            if (!completed)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_evaluation, evaluation))
                    {
                        _evaluation = null;
                        _status = PrerequisiteStatus.NotEvaluated;
                    }
                }
            }
        }
    }

    private sealed class Evaluation
    {
        public Evaluation(
            PrerequisiteProcessState owner,
            Func<CancellationToken, Task<PrerequisiteResult>> evaluator,
            CancellationToken cancellationToken,
            JoinableTaskFactory joinableTaskFactory)
        {
            Task = new AsyncLazy<PrerequisiteResult>(
                () => owner.EvaluateAndCacheAsync(this, evaluator, cancellationToken),
                joinableTaskFactory);
        }

        public AsyncLazy<PrerequisiteResult> Task { get; }
    }

    private static class ProcessStateHolder
    {
        public static PrerequisiteProcessState Instance { get; } = new(RustAnalyzerPackage.JTF);
    }
}
