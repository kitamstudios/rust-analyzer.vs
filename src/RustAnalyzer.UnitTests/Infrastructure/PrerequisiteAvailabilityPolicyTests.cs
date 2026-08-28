using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Threading;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteAvailabilityPolicyTests
{
    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated)]
    [InlineData(PrerequisiteStatus.Evaluating)]
    [InlineData(PrerequisiteStatus.Failed)]
    [InlineData(PrerequisiteStatus.Suspended)]
    public async Task EveryNonReadyStateIsUnavailableAndLogsCurrentStateOnceAsync(
        PrerequisiteStatus status)
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PrerequisiteResult> evaluation = null;
        if (status == PrerequisiteStatus.Evaluating)
        {
            evaluation = state.GetOrEvaluateAsync(_ => evaluationCompletion.Task, default);
        }
        else if (status == PrerequisiteStatus.Failed || status == PrerequisiteStatus.Suspended)
        {
            await state.GetOrEvaluateAsync(_ => Task.FromResult(CreateFailedResult()), default);
            if (status == PrerequisiteStatus.Suspended)
            {
                state.Suspend();
            }
        }

        var logger = new RecordingLogger();
        var policy = new PrerequisiteAvailabilityPolicy(state, logger, new RecordingTelemetry());

        policy.IsReady(AutomaticRustPath.LanguageClientActivation).Should().BeFalse();
        policy.IsReady(AutomaticRustPath.LanguageClientActivation).Should().BeFalse();

        logger.Lines.Should().ContainSingle();
        logger.FormatLines().Single().Should()
            .Contain("language-client activation")
            .And.Contain(status.ToString())
            .And.Contain("this Visual Studio session")
            .And.Contain("Restart Visual Studio to recheck prerequisites.");

        if (evaluation != null)
        {
            evaluationCompletion.SetResult(PrerequisiteResult.Success);
            await evaluation;
        }
    }

    [Fact]
    public async Task ReadyPassesEveryFinitePathWithoutLoggingAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(_ => Task.FromResult(PrerequisiteResult.Success), default);
        var logger = new RecordingLogger();
        var telemetry = new RecordingTelemetry();
        var policy = new PrerequisiteAvailabilityPolicy(state, logger, telemetry);

        foreach (AutomaticRustPath path in Enum.GetValues(typeof(AutomaticRustPath)))
        {
            policy.IsReady(path).Should().BeTrue();
        }

        logger.Lines.Should().BeEmpty();
        logger.Errors.Should().BeEmpty();
        telemetry.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentRepeatsLogEachFinitePathOnceAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(_ => Task.FromResult(CreateFailedResult()), default);
        state.Suspend();
        var logger = new RecordingLogger();
        var policy = new PrerequisiteAvailabilityPolicy(state, logger, new RecordingTelemetry());
        var paths = Enum.GetValues(typeof(AutomaticRustPath)).Cast<AutomaticRustPath>().ToArray();

        Parallel.ForEach(
            paths.SelectMany(path => Enumerable.Repeat(path, 32)),
            path => policy.IsReady(path).Should().BeFalse());

        logger.Lines.Should().HaveCount(paths.Length);
        logger.FormatLines().Should().OnlyContain(
            message => message.Contains("Suspended") &&
                message.Contains("Restart Visual Studio to recheck prerequisites."));
    }

    [Fact]
    public void InvalidPathIdentityFailsWithoutLogging()
    {
        using var context = new JoinableTaskContext();
        var logger = new RecordingLogger();
        var policy = new PrerequisiteAvailabilityPolicy(
            new PrerequisiteProcessState(context.Factory),
            logger,
            new RecordingTelemetry());

        Action check = () => policy.IsReady((AutomaticRustPath)int.MaxValue);

        var exception = check.Should().ThrowExactly<ArgumentOutOfRangeException>().Which;
        exception.ParamName.Should().Be("path");
        exception.Message.Should().Be(new ArgumentOutOfRangeException("path").Message);
        logger.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task GeneralSuspensionTransitionLogsOnceUnderConcurrencyAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var logger = new RecordingLogger();
        var policy = new PrerequisiteAvailabilityPolicy(state, logger, new RecordingTelemetry());
        policy.ReportSuspended();
        await state.GetOrEvaluateAsync(_ => Task.FromResult(CreateFailedResult()), default);
        state.Suspend();

        Parallel.For(0, 64, _ => policy.ReportSuspended());

        logger.Lines.Should().ContainSingle();
        logger.FormatLines().Single().Should()
            .Contain("entered prerequisite state Suspended")
            .And.Contain("Restart Visual Studio to recheck prerequisites.");
    }

    [Fact]
    public void InfoBarFailurePreservesExceptionInOneLogAndOneTelemetryEvent()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var logger = new RecordingLogger();
        var telemetry = new RecordingTelemetry();
        var policy = new PrerequisiteAvailabilityPolicy(state, logger, telemetry);
        var exceptions = Enumerable.Range(0, 32)
            .Select(index => new InvalidOperationException($"InfoBar failure {index}."))
            .ToArray();

        Parallel.ForEach(exceptions, policy.ReportInfoBarFailure);

        logger.Errors.Should().ContainSingle();
        telemetry.Exceptions.Should().ContainSingle();
        logger.Errors.Single().Arguments.Should().ContainSingle();
        logger.Errors.Single().Arguments[0].Should().BeSameAs(telemetry.Exceptions.Single());
    }

    [Fact]
    public async Task AsyncCheckAwaitsOnlyTheExistingStateEvaluationAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        var activation = state.GetOrEvaluateAsync(
            _ =>
            {
                Interlocked.Increment(ref evaluations);
                return evaluationCompletion.Task;
            },
            default);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());

        var backgroundCheck = policy.IsReadyAsync(
            AutomaticRustPath.LanguageClientActivation,
            default);

        backgroundCheck.IsCompleted.Should().BeFalse();
        evaluations.Should().Be(1);

        evaluationCompletion.SetResult(PrerequisiteResult.Success);

        (await backgroundCheck).Should().BeTrue();
        (await activation).Should().BeSameAs(PrerequisiteResult.Success);
        evaluations.Should().Be(1);
    }

    [Fact]
    public async Task AsyncCheckDoesNotInitiateAnUnevaluatedStateAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());

        (await policy.IsReadyAsync(
            AutomaticRustPath.WorkspaceMetadata,
            default)).Should().BeFalse();

        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.EvaluationCompletion.Should().BeNull();
    }

    [Fact]
    public async Task ReadyWaitObservesEvaluationStartedAfterTheWaitAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());

        var observation = policy.WaitForReadyAsync(
            AutomaticRustPath.LanguageClientActivation,
            default);

        observation.IsCompleted.Should().BeFalse();
        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.EvaluationCompletion.Should().BeNull();

        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);

        (await observation).Should().BeTrue();
    }

    [Fact]
    public async Task ReadyWaitObservesReadyAfterCanceledAttemptRetryAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());
        var observation = policy.WaitForReadyAsync(
            AutomaticRustPath.WorkspaceMetadata,
            default);
        var firstCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEvaluation = state.GetOrEvaluateAsync(_ => firstCompletion.Task, default);

        firstCompletion.SetCanceled();
        Func<Task> awaitFirst = async () => await firstEvaluation;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();

        observation.IsCompleted.Should().BeFalse();
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);

        (await observation).Should().BeTrue();
    }

    [Fact]
    public async Task ReadyWaitObservesReadyAfterFaultedAttemptRetryAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());
        var observation = policy.WaitForReadyAsync(
            AutomaticRustPath.RustTestDiscoveryExecutionHandoff,
            default);
        var expected = new InvalidOperationException("Evaluation fault.");
        var firstEvaluation = state.GetOrEvaluateAsync(
            _ => Task.FromException<PrerequisiteResult>(expected),
            default);

        Func<Task> awaitFirst = async () => await firstEvaluation;
        (await awaitFirst.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);

        observation.IsCompleted.Should().BeFalse();
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);

        (await observation).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncCheckHonorsCancellationBeforeReadyWorkAsync()
    {
        using var context = new JoinableTaskContext();
        using var cancellation = new CancellationTokenSource();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(_ => Task.FromResult(PrerequisiteResult.Success), default);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());
        cancellation.Cancel();
        Func<Task> check = async () =>
            await policy.IsReadyAsync(
                AutomaticRustPath.LanguageClientActivation,
                cancellation.Token);

        await check.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CancelingAnAsyncObserverDoesNotCancelOrReplaceEvaluationAsync()
    {
        using var context = new JoinableTaskContext();
        using var observerCancellation = new CancellationTokenSource();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        var activation = state.GetOrEvaluateAsync(
            _ =>
            {
                Interlocked.Increment(ref evaluations);
                return evaluationCompletion.Task;
            },
            default);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());
        var observer = policy.IsReadyAsync(
            AutomaticRustPath.LanguageClientActivation,
            observerCancellation.Token);

        observerCancellation.Cancel();
        Func<Task> awaitObserver = async () => await observer;

        await awaitObserver.Should().ThrowAsync<OperationCanceledException>();
        state.Status.Should().Be(PrerequisiteStatus.Evaluating);
        evaluations.Should().Be(1);

        evaluationCompletion.SetResult(PrerequisiteResult.Success);
        await activation;

        state.Status.Should().Be(PrerequisiteStatus.Ready);
        evaluations.Should().Be(1);
    }

    [Fact]
    public async Task PackageCancellationCompletesAsyncObserversAndLeavesStateRetryableAsync()
    {
        using var context = new JoinableTaskContext();
        using var packageCancellation = new CancellationTokenSource();
        var state = new PrerequisiteProcessState(context.Factory);
        var evaluations = 0;
        var activation = state.GetOrEvaluateAsync(
            cancellationToken =>
            {
                Interlocked.Increment(ref evaluations);
                var completion = new TaskCompletionSource<PrerequisiteResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => completion.TrySetCanceled());
                return completion.Task;
            },
            packageCancellation.Token);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());
        var observer = policy.IsReadyAsync(
            AutomaticRustPath.RustTestDiscoveryExecutionHandoff,
            default);

        packageCancellation.Cancel();
        Func<Task> awaitActivation = async () => await activation;
        Func<Task> awaitObserver = async () => await observer;

        await awaitActivation.Should().ThrowAsync<OperationCanceledException>();
        await awaitObserver.Should().ThrowAsync<OperationCanceledException>();
        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.EvaluationCompletion.Should().BeNull();
        evaluations.Should().Be(1);
    }

    private static PrerequisiteResult CreateFailedResult()
    {
        return PrerequisiteResult.Failed(
            new[]
            {
                new PrerequisiteFailure(
                    PrerequisiteFailureKind.CargoNotFound,
                    "Cargo was not found."),
            });
    }

    private sealed class RecordingLogger : ILogger
    {
        public ConcurrentQueue<(string Format, object[] Arguments)> Errors { get; } = new();

        public ConcurrentQueue<(string Format, object[] Arguments)> Lines { get; } = new();

        public void WriteLine(string format, params object[] args)
        {
            Lines.Enqueue((format, args));
        }

        public void WriteError(string format, params object[] args)
        {
            Errors.Enqueue((format, args));
        }

        public string[] FormatLines()
        {
            return Lines.Select(line => string.Format(line.Format, line.Arguments)).ToArray();
        }
    }

    private sealed class RecordingTelemetry : ITelemetryService
    {
        public ConcurrentQueue<Exception> Exceptions { get; } = new();

        public void TrackEvent(string eventName, params (string Key, string Value)[] properties)
        {
        }

        public void TrackException(Exception e, string siteName = null)
        {
            Exceptions.Enqueue(e);
        }

        public void TrackException(
            Exception e,
            (string Key, string Value)[] properties,
            string siteName = null)
        {
            Exceptions.Enqueue(e);
        }
    }
}
