using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.VisualStudio.Threading;

namespace KS.RustAnalyzer.Infrastructure;

public interface IPrerequisiteStartupOperations
{
    void ShowPrerequisiteFailurePrompt();

    Task<bool> ShowPrerequisiteSuspensionNotificationAsync();

    Task ShowReleaseSummaryAsync();

    Task HandleIncompatibleExtensionsAsync();

    Task InstallLatestAsync();

    Task ShowUpdateNotificationAsync();
}

public sealed class PrerequisiteStartupCoordinator
{
    private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly IPrerequisiteStartupOperations _operations;
    private readonly Func<Func<Task>, CancellationToken, Task> _runOnMainThreadAsync;
    private readonly PrerequisiteProcessState _state;
    private readonly object _sync = new();
    private AsyncLazy<object> _startup;

    public PrerequisiteStartupCoordinator(
        PrerequisiteProcessState state,
        PrerequisiteAvailabilityPolicy availabilityPolicy,
        JoinableTaskFactory joinableTaskFactory,
        Func<Func<Task>, CancellationToken, Task> runOnMainThreadAsync,
        IPrerequisiteStartupOperations operations)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _availabilityPolicy = EnsureArg.IsNotNull(
            availabilityPolicy,
            nameof(availabilityPolicy),
            options => options.WithException(
                new ArgumentNullException(nameof(availabilityPolicy))));
        _joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        _runOnMainThreadAsync = runOnMainThreadAsync ?? throw new ArgumentNullException(nameof(runOnMainThreadAsync));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public Task RunAsync(PrerequisiteResult result, CancellationToken cancellationToken)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        AsyncLazy<object> startup;
        lock (_sync)
        {
            _startup ??= new AsyncLazy<object>(
                async () =>
                {
                    await RunCoreAsync(result, cancellationToken);
                    return null;
                },
                _joinableTaskFactory);
            startup = _startup;
        }

        return startup.GetValueAsync();
    }

    private async Task RunCoreAsync(
        PrerequisiteResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            await _runOnMainThreadAsync(
                async () =>
                {
                    _operations.ShowPrerequisiteFailurePrompt();

                    if (_state.Status == PrerequisiteStatus.Suspended)
                    {
                        _availabilityPolicy.ReportSuspended();
                        try
                        {
                            await _operations.ShowPrerequisiteSuspensionNotificationAsync();
                        }
                        catch (Exception e)
                        {
                            _availabilityPolicy.ReportInfoBarFailure(e);
                        }
                    }
                },
                cancellationToken);
        }

        if (!_availabilityPolicy.IsReady(AutomaticRustPath.PackageFollowOnStartup))
        {
            return;
        }

        await _runOnMainThreadAsync(
            async () =>
            {
                await _operations.ShowReleaseSummaryAsync();
                await _operations.HandleIncompatibleExtensionsAsync();
                await _operations.InstallLatestAsync();
                await _operations.ShowUpdateNotificationAsync();
            },
            cancellationToken);
    }
}
