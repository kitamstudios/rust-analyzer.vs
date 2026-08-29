using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteSuspensionNotificationTests
{
    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Ready, false)]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Suspended, true)]
    public void EligibilityRequiresSuspendedState(PrerequisiteStatus status, bool expected)
    {
        PrerequisiteSuspensionNotification.IsEligible(status).Should().Be(expected);
    }

    [Fact]
    public async Task IneligibleStatesDoNotConsumeTheShowAttemptAsync()
    {
        using var context = new JoinableTaskContext();
        var failedResult = CreateFailedResult();
        var notEvaluated = new PrerequisiteProcessState(context.Factory);
        var ready = new PrerequisiteProcessState(context.Factory);
        await ready.GetOrEvaluateAsync(_ => Task.FromResult(PrerequisiteResult.Success), default);
        var failed = new PrerequisiteProcessState(context.Factory);
        await failed.GetOrEvaluateAsync(_ => Task.FromResult(failedResult), default);
        var evaluating = new PrerequisiteProcessState(context.Factory);
        var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluation = evaluating.GetOrEvaluateAsync(_ => evaluationCompletion.Task, default);
        var infoBar = new TestInfoBar();
        var creations = 0;
        var notification = new PrerequisiteSuspensionNotification(
            _ =>
            {
                Interlocked.Increment(ref creations);
                return Task.FromResult<IPrerequisiteSuspensionInfoBar>(infoBar);
            },
            _ => { });

        foreach (var state in new[] { notEvaluated, ready, failed, evaluating })
        {
            (await notification.ShowIfSuspendedAsync(state)).Should().BeFalse();
        }

        creations.Should().Be(0);
        infoBar.ShowCount.Should().Be(0);

        evaluationCompletion.SetResult(failedResult);
        await evaluation;
        evaluating.Suspend();

        (await notification.ShowIfSuspendedAsync(evaluating)).Should().BeTrue();
        creations.Should().Be(1);
        infoBar.ShowCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedCallersShareOneShowAttemptAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, _) = await CreateSuspendedStateAsync(context);
        var infoBar = new TestInfoBar();
        var creationCompletion =
            new TaskCompletionSource<IPrerequisiteSuspensionInfoBar>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var creations = 0;
        var notification = new PrerequisiteSuspensionNotification(
            _ =>
            {
                Interlocked.Increment(ref creations);
                return creationCompletion.Task;
            },
            _ => { });
        var calls = new ConcurrentBag<Task<bool>>();

        Parallel.For(
            0,
            16,
            _ => calls.Add(notification.ShowIfSuspendedAsync(state)));

        calls.Should().HaveCount(16);
        calls.Distinct().Should().ContainSingle();
        creations.Should().Be(1);

        creationCompletion.SetResult(infoBar);
        (await Task.WhenAll(calls)).Should().OnlyContain(shown => shown);

        var repeated = notification.ShowIfSuspendedAsync(state);
        repeated.Should().BeSameAs(calls.First());
        (await repeated).Should().BeTrue();
        creations.Should().Be(1);
        infoBar.ShowCount.Should().Be(1);
        infoBar.ActionSubscriptions.Should().Be(1);
        infoBar.ClosedSubscriptions.Should().Be(1);
    }

    [Fact]
    public async Task CloseDoesNotPersistOrPermitRecreationByTheSameNotificationAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateSuspendedStateAsync(context);
        var infoBar = new TestInfoBar();
        var creations = 0;
        var notification = CreateNotification(infoBar, () => creations++);

        (await notification.ShowIfSuspendedAsync(state)).Should().BeTrue();
        var actionContext = infoBar.Model.ActionItems.GetItem(0).ActionContext;

        infoBar.RaiseClosed();
        infoBar.RaiseAction(actionContext);

        infoBar.ActionUnsubscriptions.Should().Be(1);
        infoBar.ClosedUnsubscriptions.Should().Be(1);
        infoBar.DisposeCount.Should().Be(1);
        (await notification.ShowIfSuspendedAsync(state)).Should().BeTrue();
        creations.Should().Be(1);
        infoBar.ShowCount.Should().Be(1);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().BeSameAs(result);

        var nextInfoBar = new TestInfoBar();
        var nextCreations = 0;
        var nextNotification = CreateNotification(nextInfoBar, () => nextCreations++);

        (await nextNotification.ShowIfSuspendedAsync(state)).Should().BeTrue();
        nextCreations.Should().Be(1);
        nextInfoBar.ShowCount.Should().Be(1);
    }

    [Fact]
    public async Task OnlyTheOwnedViewPrerequisitesActionNavigatesAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateSuspendedStateAsync(context);
        var openedUrls = new List<string>();
        var infoBar = new TestInfoBar();
        var notification = new PrerequisiteSuspensionNotification(
            model =>
            {
                infoBar.Model = model;
                return Task.FromResult<IPrerequisiteSuspensionInfoBar>(infoBar);
            },
            openedUrls.Add);

        (await notification.ShowIfSuspendedAsync(state)).Should().BeTrue();
        openedUrls.Should().BeEmpty();
        var actionContext = infoBar.Model.ActionItems.GetItem(0).ActionContext;
        var otherActionContext = new PrerequisiteSuspensionInfoBarModel()
            .CreateInfoBarModel()
            .ActionItems
            .GetItem(0)
            .ActionContext;

        infoBar.RaiseAction(null);
        infoBar.RaiseAction("view_prerequisites");
        infoBar.RaiseAction(otherActionContext);
        infoBar.RaiseAction(actionContext, new object());

        openedUrls.Should().BeEmpty();

        infoBar.RaiseAction(actionContext);

        openedUrls.Should().Equal(Constants.PrerequisitesUrl);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().BeSameAs(result);
        infoBar.ShowCount.Should().Be(1);
        infoBar.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task CreationExceptionIsSharedAndNotRetriedAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, _) = await CreateSuspendedStateAsync(context);
        var expected = new InvalidOperationException("Creation failed.");
        var creations = 0;
        var notification = new PrerequisiteSuspensionNotification(
            _ =>
            {
                Interlocked.Increment(ref creations);
                return Task.FromException<IPrerequisiteSuspensionInfoBar>(expected);
            },
            _ => { });

        var first = notification.ShowIfSuspendedAsync(state);
        var second = notification.ShowIfSuspendedAsync(state);
        Func<Task> awaitFirst = async () => await first;
        Func<Task> awaitSecond = async () => await second;

        first.Should().BeSameAs(second);
        (await awaitFirst.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
        (await awaitSecond.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
        creations.Should().Be(1);
    }

    [Fact]
    public async Task MissingInfoBarFailsAndIsNotRetriedAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, _) = await CreateSuspendedStateAsync(context);
        var creations = 0;
        var notification = new PrerequisiteSuspensionNotification(
            _ =>
            {
                Interlocked.Increment(ref creations);
                return Task.FromResult<IPrerequisiteSuspensionInfoBar>(null);
            },
            _ => { });

        var first = notification.ShowIfSuspendedAsync(state);
        var second = notification.ShowIfSuspendedAsync(state);
        Func<Task> awaitFirst = async () => await first;

        first.Should().BeSameAs(second);
        (await awaitFirst.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The prerequisite suspension InfoBar could not be created.");
        creations.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowFailureIsSharedAndReleasesHandlersAsync(bool throwException)
    {
        using var context = new JoinableTaskContext();
        var (state, _) = await CreateSuspendedStateAsync(context);
        var expected = new InvalidOperationException("Show failed.");
        var infoBar = new TestInfoBar
        {
            ShowException = throwException ? expected : null,
            ShowResult = false,
        };
        var creations = 0;
        var notification = CreateNotification(infoBar, () => creations++);

        var first = notification.ShowIfSuspendedAsync(state);
        var second = notification.ShowIfSuspendedAsync(state);
        Func<Task> awaitFirst = async () => await first;

        first.Should().BeSameAs(second);
        var exception = await awaitFirst.Should().ThrowAsync<InvalidOperationException>();
        if (throwException)
        {
            exception.Which.Should().BeSameAs(expected);
        }
        else
        {
            exception.WithMessage("The prerequisite suspension InfoBar could not be shown.");
        }

        creations.Should().Be(1);
        infoBar.ShowCount.Should().Be(1);
        infoBar.ActionSubscriptions.Should().Be(1);
        infoBar.ActionUnsubscriptions.Should().Be(1);
        infoBar.ClosedSubscriptions.Should().Be(1);
        infoBar.ClosedUnsubscriptions.Should().Be(1);
        infoBar.DisposeCount.Should().Be(1);
    }

    private static PrerequisiteSuspensionNotification CreateNotification(
        TestInfoBar infoBar,
        Action created)
    {
        return new PrerequisiteSuspensionNotification(
            model =>
            {
                created();
                infoBar.Model = model;
                return Task.FromResult<IPrerequisiteSuspensionInfoBar>(infoBar);
            },
            _ => { });
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

    private static async Task<(PrerequisiteProcessState State, PrerequisiteResult Result)>
        CreateSuspendedStateAsync(JoinableTaskContext context)
    {
        var state = new PrerequisiteProcessState(context.Factory);
        var result = CreateFailedResult();
        await state.GetOrEvaluateAsync(_ => Task.FromResult(result), default);
        state.Suspend();
        return (state, result);
    }

    private sealed class TestInfoBar : IPrerequisiteSuspensionInfoBar
    {
        private EventHandler<PrerequisiteSuspensionInfoBarActionEventArgs> _actionItemClicked;
        private EventHandler _closed;

        public event EventHandler<PrerequisiteSuspensionInfoBarActionEventArgs> ActionItemClicked
        {
            add
            {
                ActionSubscriptions++;
                _actionItemClicked += value;
            }

            remove
            {
                ActionUnsubscriptions++;
                _actionItemClicked -= value;
            }
        }

        public event EventHandler Closed
        {
            add
            {
                ClosedSubscriptions++;
                _closed += value;
            }

            remove
            {
                ClosedUnsubscriptions++;
                _closed -= value;
            }
        }

        public InfoBarModel Model { get; set; }

        public bool ShowResult { get; set; } = true;

        public Exception ShowException { get; set; }

        public int ShowCount { get; private set; }

        public int ActionSubscriptions { get; private set; }

        public int ActionUnsubscriptions { get; private set; }

        public int ClosedSubscriptions { get; private set; }

        public int ClosedUnsubscriptions { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<bool> TryShowAsync()
        {
            ShowCount++;
            return ShowException == null
                ? Task.FromResult(ShowResult)
                : Task.FromException<bool>(ShowException);
        }

        public void RaiseAction(object actionContext, object sender = null)
        {
            _actionItemClicked?.Invoke(
                sender ?? this,
                new PrerequisiteSuspensionInfoBarActionEventArgs(actionContext));
        }

        public void RaiseClosed()
        {
            _closed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
