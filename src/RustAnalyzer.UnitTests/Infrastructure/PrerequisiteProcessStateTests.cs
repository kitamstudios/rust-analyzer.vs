using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio.Threading;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteProcessStateTests
{
    [Fact]
    public void InitialStateIsUnavailable()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);

        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.IsAvailable.Should().BeFalse();
        state.CachedResult.Should().BeNull();
    }

    [Fact]
    public async Task SuccessfulEvaluationMakesStateAvailableAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);

        var result = await state.GetOrEvaluateAsync(_ => Task.FromResult(PrerequisiteResult.Success), default);

        result.Should().BeSameAs(PrerequisiteResult.Success);
        state.Status.Should().Be(PrerequisiteStatus.Ready);
        state.IsAvailable.Should().BeTrue();
        state.CachedResult.Should().BeSameAs(result);
    }

    [Fact]
    public async Task FailedEvaluationCachesImmutableFailuresAsync()
    {
        var failures = new List<PrerequisiteFailure>
        {
            new("rustup", "rustup was not found."),
            new("cargo", "Cargo was not found."),
        };
        var expected = PrerequisiteResult.Failed(failures);
        failures.Clear();
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);

        var result = await state.GetOrEvaluateAsync(_ => Task.FromResult(expected), default);

        result.Should().BeSameAs(expected);
        result.Failures.Should().HaveCount(2);
        result.Failures.Select(failure => failure.Check).Should().Equal("rustup", "cargo");
        state.Status.Should().Be(PrerequisiteStatus.Failed);
        state.IsAvailable.Should().BeFalse();
        state.CachedResult.Should().BeSameAs(result);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneEvaluationAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        var calls = new ConcurrentBag<Task<PrerequisiteResult>>();

        Parallel.For(
            0,
            16,
            _ => calls.Add(
                state.GetOrEvaluateAsync(
                    _ =>
                    {
                        Interlocked.Increment(ref evaluations);
                        return evaluationCompletion.Task;
                    },
                    default)));

        calls.Should().HaveCount(16);
        evaluations.Should().Be(1);
        state.Status.Should().Be(PrerequisiteStatus.Evaluating);
        state.IsAvailable.Should().BeFalse();

        evaluationCompletion.SetResult(PrerequisiteResult.Success);
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(result => ReferenceEquals(result, PrerequisiteResult.Success));
        state.Status.Should().Be(PrerequisiteStatus.Ready);
    }

    [Fact]
    public async Task EvaluationCompletionOnlyObservesTheExistingEvaluationAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;

        state.EvaluationCompletion.Should().BeNull();
        state.GetEvaluationStatus(out var missingObservation).Should()
            .Be(PrerequisiteStatus.NotEvaluated);
        missingObservation.Should().BeNull();
        var activation = state.GetOrEvaluateAsync(
            _ =>
            {
                Interlocked.Increment(ref evaluations);
                return evaluationCompletion.Task;
            },
            default);
        var observation = state.EvaluationCompletion;
        state.GetEvaluationStatus(out var atomicObservation).Should()
            .Be(PrerequisiteStatus.Evaluating);

        observation.Should().BeSameAs(activation);
        atomicObservation.Should().BeSameAs(observation);
        evaluations.Should().Be(1);
        state.Status.Should().Be(PrerequisiteStatus.Evaluating);

        evaluationCompletion.SetResult(PrerequisiteResult.Success);

        (await observation).Should().BeSameAs(PrerequisiteResult.Success);
        state.Status.Should().Be(PrerequisiteStatus.Ready);
        evaluations.Should().Be(1);
    }

    [Fact]
    public async Task CompletedEvaluationIsReusedAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluations = 0;
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync = _ =>
        {
            Interlocked.Increment(ref evaluations);
            return Task.FromResult(PrerequisiteResult.Success);
        };

        var firstTask = state.GetOrEvaluateAsync(evaluateAsync, default);
        await firstTask;
        var secondTask = state.GetOrEvaluateAsync(evaluateAsync, default);

        (await secondTask).Should().BeSameAs(PrerequisiteResult.Success);
        evaluations.Should().Be(1);
    }

    [Fact]
    public async Task FailedEvaluationCanTransitionToSuspendedAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var expected = PrerequisiteResult.Failed(new[] { new PrerequisiteFailure("cargo", "Cargo was not found.") });
        var evaluations = 0;
        await state.GetOrEvaluateAsync(
            _ =>
            {
                Interlocked.Increment(ref evaluations);
                return Task.FromResult(expected);
            },
            default);

        state.Suspend();
        var cached = await state.GetOrEvaluateAsync(
            _ =>
            {
                Interlocked.Increment(ref evaluations);
                return Task.FromResult(PrerequisiteResult.Success);
            },
            default);

        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.IsAvailable.Should().BeFalse();
        state.CachedResult.Should().BeSameAs(expected);
        cached.Should().BeSameAs(expected);
        evaluations.Should().Be(1);
    }

    [Fact]
    public async Task SuspensionRequiresFailedEvaluationAsync()
    {
        using var context = new JoinableTaskContext();
        var notEvaluated = new PrerequisiteProcessState(context.Factory);
        var ready = new PrerequisiteProcessState(context.Factory);
        await ready.GetOrEvaluateAsync(_ => Task.FromResult(PrerequisiteResult.Success), default);

        Action suspendNotEvaluated = notEvaluated.Suspend;
        Action suspendReady = ready.Suspend;

        suspendNotEvaluated.Should().Throw<InvalidOperationException>();
        suspendReady.Should().Throw<InvalidOperationException>();
        notEvaluated.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        ready.Status.Should().Be(PrerequisiteStatus.Ready);
    }

    [Fact]
    public async Task EvaluationCannotBeSuspendedWhileInProgressAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var completion = new TaskCompletionSource<PrerequisiteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluation = state.GetOrEvaluateAsync(_ => completion.Task, default);
        Action suspend = state.Suspend;

        suspend.Should().Throw<InvalidOperationException>();
        state.Status.Should().Be(PrerequisiteStatus.Evaluating);

        completion.SetResult(PrerequisiteResult.Success);
        await evaluation;
        state.Status.Should().Be(PrerequisiteStatus.Ready);
    }

    [Fact]
    public async Task SuspendedStateCannotTransitionAgainAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var failed = PrerequisiteResult.Failed(new[] { new PrerequisiteFailure("cargo", "Cargo was not found.") });
        await state.GetOrEvaluateAsync(_ => Task.FromResult(failed), default);
        state.Suspend();

        Action suspend = state.Suspend;

        suspend.Should().Throw<InvalidOperationException>();
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
    }

    [Fact]
    public async Task CanceledEvaluationIsNotCachedAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var firstEvaluation = new TaskCompletionSource<PrerequisiteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync = _ =>
        {
            return Interlocked.Increment(ref evaluations) == 1
                ? firstEvaluation.Task
                : Task.FromResult(PrerequisiteResult.Success);
        };

        var firstTask = state.GetOrEvaluateAsync(evaluateAsync, default);
        var concurrentTask = state.GetOrEvaluateAsync(evaluateAsync, default);
        firstEvaluation.SetCanceled();

        Func<Task> awaitFirst = async () => await firstTask;
        Func<Task> awaitConcurrent = async () => await concurrentTask;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();
        await awaitConcurrent.Should().ThrowAsync<OperationCanceledException>();
        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.IsAvailable.Should().BeFalse();
        state.CachedResult.Should().BeNull();

        var result = await state.GetOrEvaluateAsync(evaluateAsync, default);

        result.Should().BeSameAs(PrerequisiteResult.Success);
        evaluations.Should().Be(2);
        state.Status.Should().Be(PrerequisiteStatus.Ready);
    }

    [Fact]
    public async Task CapturedCanceledCompletionDoesNotRetargetToRetryAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var firstCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEvaluation = state.GetOrEvaluateAsync(_ => firstCompletion.Task, default);
        var capturedCompletion = state.EvaluationCompletion;

        firstCompletion.SetCanceled();
        Func<Task> awaitFirst = async () => await firstEvaluation;
        Func<Task> awaitCaptured = async () => await capturedCompletion;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();
        await awaitCaptured.Should().ThrowAsync<OperationCanceledException>();

        var retryCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = state.GetOrEvaluateAsync(_ => retryCompletion.Task, default);

        state.EvaluationCompletion.Should().BeSameAs(retry);
        state.EvaluationCompletion.Should().NotBeSameAs(capturedCompletion);
        capturedCompletion.IsCanceled.Should().BeTrue();

        retryCompletion.SetResult(PrerequisiteResult.Success);
        (await retry).Should().BeSameAs(PrerequisiteResult.Success);
    }

    [Fact]
    public async Task CapturedFaultedCompletionDoesNotRetargetToRetryAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var expected = new InvalidOperationException("Evaluation fault.");
        var firstCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEvaluation = state.GetOrEvaluateAsync(
            _ => firstCompletion.Task,
            default);
        var capturedCompletion = state.EvaluationCompletion;

        firstCompletion.SetException(expected);
        Func<Task> awaitFirst = async () => await firstEvaluation;
        Func<Task> awaitCaptured = async () => await capturedCompletion;
        (await awaitFirst.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
        (await awaitCaptured.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);

        var retryCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = state.GetOrEvaluateAsync(_ => retryCompletion.Task, default);

        state.EvaluationCompletion.Should().BeSameAs(retry);
        state.EvaluationCompletion.Should().NotBeSameAs(capturedCompletion);
        capturedCompletion.IsFaulted.Should().BeTrue();

        retryCompletion.SetResult(PrerequisiteResult.Success);
        (await retry).Should().BeSameAs(PrerequisiteResult.Success);
    }

    [Fact]
    public async Task EvaluatorFaultDoesNotMakeStateAvailableAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var expected = new InvalidOperationException("Unexpected evaluator fault.");
        var evaluations = 0;
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync = _ =>
        {
            return Interlocked.Increment(ref evaluations) == 1
                ? Task.FromException<PrerequisiteResult>(expected)
                : Task.FromResult(PrerequisiteResult.Success);
        };

        var evaluation = state.GetOrEvaluateAsync(evaluateAsync, default);
        Func<Task> awaitEvaluation = async () => await evaluation;

        var exception = await awaitEvaluation.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Should().BeSameAs(expected);
        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.IsAvailable.Should().BeFalse();
        state.CachedResult.Should().BeNull();

        var result = await state.GetOrEvaluateAsync(evaluateAsync, default);

        result.Should().BeSameAs(PrerequisiteResult.Success);
        evaluations.Should().Be(2);
        state.Status.Should().Be(PrerequisiteStatus.Ready);
    }
}
