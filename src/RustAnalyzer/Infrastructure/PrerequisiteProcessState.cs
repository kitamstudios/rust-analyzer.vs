using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
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

public enum PrerequisiteFailureKind
{
    Unclassified,
    UnsupportedVisualStudioHost,
    RustupNotFound,
    RustupNotOperational,
    DefaultToolchainNotConfigured,
    CargoNotFound,
    CargoNotOperational,
    PrerequisiteEvaluationFailed,
}

public sealed class PrerequisiteFailure
{
    public PrerequisiteFailure(string check, string message)
        : this(PrerequisiteFailureKind.Unclassified, check, message)
    {
    }

    public PrerequisiteFailure(PrerequisiteFailureKind kind, string message)
        : this(kind, kind.ToString(), message)
    {
    }

    private PrerequisiteFailure(PrerequisiteFailureKind kind, string check, string message)
    {
        Kind = kind;
        Check = EnsureArg.IsNotNull(
            check,
            nameof(check),
            options => options.WithException(new ArgumentNullException(nameof(check))));
        Message = EnsureArg.IsNotNull(
            message,
            nameof(message),
            options => options.WithException(new ArgumentNullException(nameof(message))));
    }

    public PrerequisiteFailureKind Kind { get; }

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
        EnsureArg.IsNotNull(
            failures,
            nameof(failures),
            options => options.WithException(new ArgumentNullException(nameof(failures))));

        var immutableFailures = ImmutableArray.CreateRange(failures);
        EnsureArg.IsFalse(
            immutableFailures.IsEmpty,
            nameof(failures),
            options => options.WithException(
                new ArgumentException(
                    "A failed result requires at least one failure.",
                    nameof(failures))));

        foreach (var failure in immutableFailures)
        {
            EnsureArg.IsNotNull(
                failure,
                nameof(failures),
                options => options.WithException(
                    new ArgumentException("Failures cannot contain null.", nameof(failures))));
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
    private TaskCompletionSource<PrerequisiteStatus> _statusChanged =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PrerequisiteProcessState(JoinableTaskFactory joinableTaskFactory)
    {
        _joinableTaskFactory = EnsureArg.IsNotNull(
            joinableTaskFactory,
            nameof(joinableTaskFactory),
            options => options.WithException(new ArgumentNullException(nameof(joinableTaskFactory))));
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

    public Task<PrerequisiteResult> EvaluationCompletion
    {
        get
        {
            Evaluation evaluation;
            lock (_sync)
            {
                evaluation = _evaluation;
            }

            return evaluation?.Completion;
        }
    }

    public PrerequisiteStatus GetEvaluationStatus(
        out Task<PrerequisiteResult> evaluationCompletion)
    {
        lock (_sync)
        {
            evaluationCompletion = _evaluation?.Completion;
            return _status;
        }
    }

    public Task<PrerequisiteResult> GetOrEvaluateAsync(
        Func<CancellationToken, Task<PrerequisiteResult>> evaluator,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(
            evaluator,
            nameof(evaluator),
            options => options.WithException(new ArgumentNullException(nameof(evaluator))));

        Evaluation evaluation;
        lock (_sync)
        {
            if (_evaluation == null)
            {
                SetStatusUnderLock(PrerequisiteStatus.Evaluating);
                _evaluation = new Evaluation(this, evaluator, cancellationToken, _joinableTaskFactory);
            }

            evaluation = _evaluation;
        }

        return evaluation.StartAsync();
    }

    public Task<PrerequisiteStatus> WaitForStatusChangeAsync(
        PrerequisiteStatus observedStatus,
        CancellationToken cancellationToken)
    {
        Task<PrerequisiteStatus> statusChanged;
        lock (_sync)
        {
            if (_status != observedStatus)
            {
                return Task.FromResult(_status);
            }

            statusChanged = _statusChanged.Task;
        }

        return ThreadingTools.WithCancellation(statusChanged, cancellationToken);
    }

    public void Suspend()
    {
        lock (_sync)
        {
            if (_status != PrerequisiteStatus.Failed)
            {
                throw new InvalidOperationException("Prerequisites can be suspended only after a failed evaluation.");
            }

            SetStatusUnderLock(PrerequisiteStatus.Suspended);
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

            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                _result = result;
                SetStatusUnderLock(
                    result.IsSuccess ? PrerequisiteStatus.Ready : PrerequisiteStatus.Failed);
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
                        SetStatusUnderLock(PrerequisiteStatus.NotEvaluated);
                    }
                }
            }
        }
    }

    private void SetStatusUnderLock(PrerequisiteStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        var statusChanged = _statusChanged;
        _statusChanged = new TaskCompletionSource<PrerequisiteStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        statusChanged.TrySetResult(status);
    }

    private sealed class Evaluation
    {
        private readonly CancellationToken _cancellationToken;
        private readonly AsyncLazy<PrerequisiteResult> _completion;
        private readonly Func<CancellationToken, Task<PrerequisiteResult>> _evaluator;
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly PrerequisiteProcessState _owner;
        private readonly SemaphoreSlim _started = new(0, 1);
        private JoinableTask<PrerequisiteResult> _evaluation;
        private int _startClaimed;

        public Evaluation(
            PrerequisiteProcessState owner,
            Func<CancellationToken, Task<PrerequisiteResult>> evaluator,
            CancellationToken cancellationToken,
            JoinableTaskFactory joinableTaskFactory)
        {
            _owner = owner;
            _evaluator = evaluator;
            _cancellationToken = cancellationToken;
            _joinableTaskFactory = joinableTaskFactory;
            _completion = new AsyncLazy<PrerequisiteResult>(
                GetCompletionAsync,
                joinableTaskFactory);
        }

        public Task<PrerequisiteResult> Completion => _completion.GetValueAsync();

        public Task<PrerequisiteResult> StartAsync()
        {
            if (Interlocked.CompareExchange(ref _startClaimed, 1, 0) == 0)
            {
                _evaluation = _joinableTaskFactory.RunAsync(
                    () => _owner.EvaluateAndCacheAsync(this, _evaluator, _cancellationToken));
                _started.Release();
            }

            return _completion.GetValueAsync();
        }

        private async Task<PrerequisiteResult> GetCompletionAsync()
        {
            await _started.WaitAsync();
            return await _evaluation;
        }
    }

    private static class ProcessStateHolder
    {
        public static PrerequisiteProcessState Instance { get; } = new(RustAnalyzerPackage.JTF);
    }
}
