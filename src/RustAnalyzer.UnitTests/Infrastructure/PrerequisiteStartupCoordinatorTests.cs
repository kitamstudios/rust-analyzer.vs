using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Threading;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteStartupCoordinatorTests
{
    private const string Evaluation = "evaluation";
    private const string Prompt = "prompt";
    private const string InfoBar = "InfoBar";
    private const string ReleaseSummary = "release summary";
    private const string IncompatibleExtensions = "incompatible extensions";
    private const string Installer = "installer";
    private const string UpdateNotification = "update notification";

    [Fact]
    public async Task ReadyEvaluationRunsNormalStartupInExactOrderOnceAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var operations = new TestStartupOperations(state, calls);
        var coordinator = CreateCoordinator(context, state, operations);

        await EvaluateAndRunAsync(
            coordinator,
            state,
            calls,
            PrerequisiteResult.Success,
            default);
        await EvaluateAndRunAsync(
            coordinator,
            state,
            calls,
            PrerequisiteResult.Success,
            default);

        calls.Should().Equal(
            Evaluation,
            ReleaseSummary,
            IncompatibleExtensions,
            Installer,
            UpdateNotification);
        state.Status.Should().Be(PrerequisiteStatus.Ready);
    }

    [Fact]
    public async Task DisableShowsPromptAndInfoBarWithoutNormalStartupAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var operations = new TestStartupOperations(state, calls)
        {
            PromptAction = state.Suspend,
        };
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            context,
            state,
            operations,
            new PrerequisiteAvailabilityPolicy(state, logger, new RecordingTelemetry()));

        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);
        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);

        calls.Should().Equal(Evaluation, Prompt, InfoBar);
        logger.Lines.Select(line => string.Format(line.Format, line.Arguments)).Should()
            .ContainSingle(message => message.Contains("entered prerequisite state Suspended"))
            .And.ContainSingle(message => message.Contains("package follow-on startup"));
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().NotBeNull();
    }

    [Fact]
    public async Task ShutdownPromptResultStopsStartupInFailedStateAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var operations = new TestStartupOperations(state, calls)
        {
            PromptAction = () =>
            {
                var prompt = new PrerequisiteFailurePromptController(
                    state,
                    (_, _, _, _, _) => VSConstants.MessageBoxResult.IDCANCEL,
                    _ => throw new InvalidOperationException("Unexpected navigation."));
                prompt.Show();
            },
        };
        var coordinator = CreateCoordinator(context, state, operations);

        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);
        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);

        calls.Should().Equal(Evaluation, Prompt);
        state.Status.Should().Be(PrerequisiteStatus.Failed);
        state.CachedResult.Should().NotBeNull();
    }

    [Fact]
    public async Task HelpLoopDoesNotRepeatEvaluationAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var responses = new Queue<VSConstants.MessageBoxResult>(
            new[]
            {
                VSConstants.MessageBoxResult.IDNO,
                VSConstants.MessageBoxResult.IDYES,
            });
        var presentations = 0;
        var navigations = 0;
        var operations = new TestStartupOperations(state, calls)
        {
            PromptAction = () =>
            {
                var prompt = new PrerequisiteFailurePromptController(
                    state,
                    (_, _, _, _, _) =>
                    {
                        presentations++;
                        return responses.Dequeue();
                    },
                    _ => navigations++);
                prompt.Show();
            },
        };
        var coordinator = CreateCoordinator(context, state, operations);

        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);
        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);

        calls.Should().Equal(Evaluation, Prompt, InfoBar);
        presentations.Should().Be(2);
        navigations.Should().Be(1);
        responses.Should().BeEmpty();
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
    }

    [Fact]
    public async Task NullHostLookupIsCachedAndPromptsThroughActivationAsync()
    {
        using var context = new JoinableTaskContext();
        var probe = new HostVersionPrerequisiteProbe(_ => Task.FromResult<Version>(null));
        var logger = new RecordingLogger();
        var telemetry = new RecordingTelemetry();
        var service = new PreReqsCheckService(probe, telemetry, logger);
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var operations = new TestStartupOperations(state, calls)
        {
            PromptAction = state.Suspend,
        };
        var coordinator = new PrerequisiteStartupCoordinator(
            state,
            new PrerequisiteAvailabilityPolicy(state, logger, telemetry),
            context.Factory,
            RunInlineAsync,
            operations);
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync =
            async cancellationToken =>
            {
                calls.Add(Evaluation);
                return await service.EvaluateAsync(cancellationToken);
            };

        await EvaluateAndRunAsync(coordinator, state, evaluateAsync, default);
        await EvaluateAndRunAsync(coordinator, state, evaluateAsync, default);

        calls.Should().Equal(Evaluation, Prompt, InfoBar);
        probe.HostLookupCount.Should().Be(1);
        probe.RunCount.Should().Be(3);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Failures.Should().ContainSingle();
        state.CachedResult.Failures[0].Kind.Should().Be(PrerequisiteFailureKind.UnsupportedVisualStudioHost);
        logger.Errors.Should().BeEmpty();
        telemetry.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ThrowingHostLookupBecomesOneTypedCachedFailureAndSuspendsAsync()
    {
        await AssertUnexpectedHostFaultAsync(
            new InvalidOperationException("Private host probe diagnostics."));
    }

    [Fact]
    public async Task UncanceledHostOperationCanceledFaultBecomesTypedFailureAsync()
    {
        await AssertUnexpectedHostFaultAsync(
            new OperationCanceledException("The host probe canceled independently."));
    }

    [Fact]
    public async Task CancellationLeavesStateRetryableAndRunsNoUiOrNormalWorkAsync()
    {
        using var context = new JoinableTaskContext();
        using var cancellation = new CancellationTokenSource();
        var firstEvaluation = new TaskCompletionSource<PrerequisiteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var operations = new TestStartupOperations(state, calls);
        var coordinator = new PrerequisiteStartupCoordinator(
            state,
            CreatePolicy(state),
            context.Factory,
            RunInlineAsync,
            operations);
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync =
            _ =>
            {
                return Interlocked.Increment(ref evaluations) == 1
                    ? firstEvaluation.Task
                    : Task.FromResult(PrerequisiteResult.Success);
            };

        var firstRun = EvaluateAndRunAsync(coordinator, state, evaluateAsync, cancellation.Token);
        cancellation.Cancel();
        firstEvaluation.SetResult(PrerequisiteResult.Success);
        Func<Task> awaitFirstRun = async () => await firstRun;

        await awaitFirstRun.Should().ThrowAsync<OperationCanceledException>();
        state.Status.Should().Be(PrerequisiteStatus.NotEvaluated);
        state.CachedResult.Should().BeNull();
        calls.Should().BeEmpty();

        await EvaluateAndRunAsync(coordinator, state, evaluateAsync, default);

        evaluations.Should().Be(2);
        state.Status.Should().Be(PrerequisiteStatus.Ready);
        calls.Should().Equal(
            ReleaseSummary,
            IncompatibleExtensions,
            Installer,
            UpdateNotification);
    }

    [Fact]
    public async Task EvaluationServicePropagatesCancellationWithoutDiagnosticsAsync()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var logger = new RecordingLogger();
        var telemetry = new RecordingTelemetry();
        var service = new PreReqsCheckService(
            new HostVersionPrerequisiteProbe(_ => Task.FromResult(new Version(17, 12))),
            telemetry,
            logger);
        Func<Task> evaluate = async () => await service.EvaluateAsync(cancellation.Token);

        await evaluate.Should().ThrowAsync<OperationCanceledException>();

        logger.Errors.Should().BeEmpty();
        telemetry.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task InfoBarFailureIsReportedOnceWithoutRetryOrNormalStartupAsync()
    {
        using var context = new JoinableTaskContext();
        var expected = new InvalidOperationException("InfoBar failed.");
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        var operations = new TestStartupOperations(state, calls)
        {
            PromptAction = state.Suspend,
            InfoBarAction = () => Task.FromException<bool>(expected),
        };
        var logger = new RecordingLogger();
        var telemetry = new RecordingTelemetry();
        var coordinator = CreateCoordinator(
            context,
            state,
            operations,
            new PrerequisiteAvailabilityPolicy(state, logger, telemetry));

        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);
        await EvaluateAndRunAsync(coordinator, state, calls, CreateFailedResult(), default);

        calls.Should().Equal(Evaluation, Prompt, InfoBar);
        logger.Errors.Should().ContainSingle();
        logger.Errors[0].Arguments.Should().ContainSingle().Which.Should().BeSameAs(expected);
        telemetry.Exceptions.Should().Equal(expected);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
    }

    [Fact]
    public void ConcurrentAndRepeatedRunsDoNotDuplicateEvaluationOrNormalStartup()
    {
        using var context = new JoinableTaskContext();
        context.Factory.Run(
            async () =>
            {
                var evaluationCompletion = new TaskCompletionSource<PrerequisiteResult>();
                var evaluations = 0;
                var state = new PrerequisiteProcessState(context.Factory);
                var calls = new List<string>();
                var operations = new TestStartupOperations(state, calls);
                var coordinator = new PrerequisiteStartupCoordinator(
                    state,
                    CreatePolicy(state),
                    context.Factory,
                    RunInlineAsync,
                    operations);
                Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync =
                    _ =>
                    {
                        Interlocked.Increment(ref evaluations);
                        return evaluationCompletion.Task;
                    };
                var runs = new ConcurrentBag<Task>();
                runs.Add(EvaluateAndRunAsync(coordinator, state, evaluateAsync, default));

                Parallel.For(
                    0,
                    15,
                    _ => runs.Add(EvaluateAndRunAsync(coordinator, state, evaluateAsync, default)));

                runs.Should().HaveCount(16);
                evaluations.Should().Be(1);

                evaluationCompletion.SetResult(PrerequisiteResult.Success);
                await Task.WhenAll(runs);
                await EvaluateAndRunAsync(coordinator, state, evaluateAsync, default);

                evaluations.Should().Be(1);
                calls.Should().Equal(
                    ReleaseSummary,
                    IncompatibleExtensions,
                    Installer,
                    UpdateNotification);
            });
    }

    [Fact]
    public void LegacyPrerequisitePathIsRemovedWithoutChangingIncompatibleRestart()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath),
                "..",
                "..",
                ".."));
        if (!Directory.Exists(Path.Combine(repositoryRoot, "src", "RustAnalyzer")))
        {
            repositoryRoot = Path.GetFullPath(Path.Combine(repositoryRoot, ".."));
        }

        var prerequisiteSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "RustAnalyzer", "Infrastructure", "PreReqsCheckService.cs"));
        var productRoot = Path.Combine(repositoryRoot, "src", "RustAnalyzer");
        var evaluatorSource = File.ReadAllText(
            Path.Combine(productRoot, "Infrastructure", "PrerequisiteEvaluator.cs"));
        var coordinatorSource = File.ReadAllText(
            Path.Combine(productRoot, "Infrastructure", "PrerequisiteStartupCoordinator.cs"));
        var availabilityPolicySource = File.ReadAllText(
            Path.Combine(productRoot, "Infrastructure", "PrerequisiteAvailabilityPolicy.cs"));
        var packagePath = Path.Combine(productRoot, "RustAnalyzerPackage.cs");
        var packageSource = File.ReadAllText(packagePath);

        foreach (var legacySymbol in new[]
        {
            "_preReqChecks",
            "_cargoService",
            "SatisfyAsync",
            "DoChecksAsync",
            "CheckRustupToolchainInstallationAsync",
            "CheckRustupAsync",
            "CheckCargoAsync",
            "VsVersionCheck",
        })
        {
            prerequisiteSource.Should().NotContain(legacySymbol);
        }

        prerequisiteSource.Should().NotContain("OpenSystemBrowser").And.NotContain("RestartAsync");
        coordinatorSource.Should().NotContain("OpenSystemBrowser").And.NotContain("RestartAsync");
        packageSource.Should().NotContain("_preReqs.SatisfyAsync");
        availabilityPolicySource.Should().Contain(
            "_logger.WriteError(\"Failed to show prerequisite suspension InfoBar. Ex: {0}\", exception);");
        availabilityPolicySource.Should().Contain("_telemetry.TrackException(exception);");
        packageSource.Should().NotContain("CommunityVS.Shell.GetVsVersionAsync()");
        evaluatorSource
            .Split(new[] { "CommunityVS.Shell.GetVsVersionAsync()" }, StringSplitOptions.None)
            .Should().HaveCount(2);
        evaluatorSource.Should().Contain("Environment.SetEnvironmentVariable(")
            .And.Contain("Constants.RAVsVersion")
            .And.Contain("version?.ToString()")
            .And.Contain("EnvironmentVariableTarget.Process");

        var evaluationCallers = Directory
            .EnumerateFiles(productRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) < 0)
            .Where(path => path.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) < 0)
            .Where(path => File.ReadAllText(path).IndexOf(".GetOrEvaluateAsync(", StringComparison.Ordinal) >= 0);
        evaluationCallers.Should().Equal(packagePath);

        var activationStart = packageSource.IndexOf(
            "protected override async Task OnAfterPackageLoadedAsync",
            StringComparison.Ordinal);
        var activationEnd = packageSource.IndexOf(
            "private static async Task RunOnMainThreadAsync",
            activationStart,
            StringComparison.Ordinal);
        activationStart.Should().BeGreaterThan(-1);
        activationEnd.Should().BeGreaterThan(activationStart);
        var activationMethod = packageSource.Substring(activationStart, activationEnd - activationStart);
        activationMethod.IndexOf("GetOrEvaluateAsync", StringComparison.Ordinal)
            .Should().BeLessThan(
                activationMethod.IndexOf("_startupCoordinator.RunAsync", StringComparison.Ordinal));
        activationMethod.Should().Contain("_preReqs.EvaluateAsync").And.Contain("cancellationToken");
        activationMethod.Should().NotContain("ReleaseSummaryNotification")
            .And.NotContain("SearchAndDisableIncompatibleExtensionsAsync")
            .And.NotContain("InstallLatestAsync")
            .And.NotContain("RlsUpdatedNotification");

        var methodStart = packageSource.IndexOf(
            "private async Task SearchAndDisableIncompatibleExtensionsAsync()",
            StringComparison.Ordinal);
        var methodEnd = packageSource.IndexOf(
            "private static IReadOnlyList",
            methodStart,
            StringComparison.Ordinal);
        methodStart.Should().BeGreaterThan(-1);
        methodEnd.Should().BeGreaterThan(methodStart);
        var incompatibleExtensionMethod = packageSource.Substring(methodStart, methodEnd - methodStart);
        incompatibleExtensionMethod
            .Split(new[] { "CommunityVS.Shell.RestartAsync" }, StringSplitOptions.None)
            .Should().HaveCount(2);
    }

    private static async Task AssertUnexpectedHostFaultAsync(Exception expected)
    {
        using var context = new JoinableTaskContext();
        var probe = new HostVersionPrerequisiteProbe(
            _ => Task.FromException<Version>(expected));
        var logger = new RecordingLogger();
        var telemetry = new RecordingTelemetry();
        var service = new PreReqsCheckService(probe, telemetry, logger);
        var state = new PrerequisiteProcessState(context.Factory);
        var calls = new List<string>();
        string promptMessage = null;
        var operations = new TestStartupOperations(state, calls)
        {
            PromptAction = () =>
            {
                var prompt = new PrerequisiteFailurePromptController(
                    state,
                    (_, message, _, _, _) =>
                    {
                        promptMessage = message;
                        return VSConstants.MessageBoxResult.IDYES;
                    },
                    _ => throw new InvalidOperationException("Unexpected navigation."));
                prompt.Show();
            },
        };
        var coordinator = CreateCoordinator(context, state, operations);
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync =
            async cancellationToken =>
            {
                calls.Add(Evaluation);
                return await service.EvaluateAsync(cancellationToken);
            };

        await EvaluateAndRunAsync(coordinator, state, evaluateAsync, default);
        await EvaluateAndRunAsync(coordinator, state, evaluateAsync, default);

        calls.Should().Equal(Evaluation, Prompt, InfoBar);
        probe.HostLookupCount.Should().Be(1);
        probe.RunCount.Should().Be(0);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Failures.Should().ContainSingle();
        state.CachedResult.Failures[0].Kind.Should().Be(PrerequisiteFailureKind.PrerequisiteEvaluationFailed);
        promptMessage.Should().Contain("Review Output > rust-analyzer.vs");
        promptMessage.Should().NotContain(expected.Message);
        logger.Errors.Should().HaveCount(1);
        string.Format(logger.Errors[0].Format, logger.Errors[0].Arguments)
            .Should().Contain(expected.ToString());
        telemetry.Exceptions.Should().Equal(expected);
    }

    private static PrerequisiteStartupCoordinator CreateCoordinator(
        JoinableTaskContext context,
        PrerequisiteProcessState state,
        IPrerequisiteStartupOperations operations,
        PrerequisiteAvailabilityPolicy availabilityPolicy = null)
    {
        return new PrerequisiteStartupCoordinator(
            state,
            availabilityPolicy ?? CreatePolicy(state),
            context.Factory,
            RunInlineAsync,
            operations);
    }

    private static PrerequisiteAvailabilityPolicy CreatePolicy(PrerequisiteProcessState state)
    {
        return new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger(),
            new RecordingTelemetry());
    }

    private static Task EvaluateAndRunAsync(
        PrerequisiteStartupCoordinator coordinator,
        PrerequisiteProcessState state,
        List<string> calls,
        PrerequisiteResult result,
        CancellationToken cancellationToken)
    {
        return EvaluateAndRunAsync(
            coordinator,
            state,
            _ =>
            {
                calls.Add(Evaluation);
                return Task.FromResult(result);
            },
            cancellationToken);
    }

    private static async Task EvaluateAndRunAsync(
        PrerequisiteStartupCoordinator coordinator,
        PrerequisiteProcessState state,
        Func<CancellationToken, Task<PrerequisiteResult>> evaluateAsync,
        CancellationToken cancellationToken)
    {
        var result = await state.GetOrEvaluateAsync(evaluateAsync, cancellationToken);
        await coordinator.RunAsync(result, cancellationToken);
    }

    private static Task RunInlineAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return action();
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

    private sealed class TestStartupOperations : IPrerequisiteStartupOperations
    {
        private readonly List<string> _calls;
        private readonly PrerequisiteProcessState _state;

        public TestStartupOperations(PrerequisiteProcessState state, List<string> calls)
        {
            _state = state;
            _calls = calls;
        }

        public Action PromptAction { get; set; }

        public Func<Task<bool>> InfoBarAction { get; set; } = () => Task.FromResult(true);

        public void ShowPrerequisiteFailurePrompt()
        {
            _calls.Add(Prompt);
            _state.Status.Should().Be(PrerequisiteStatus.Failed);
            PromptAction?.Invoke();
        }

        public async Task<bool> ShowPrerequisiteSuspensionNotificationAsync()
        {
            _calls.Add(InfoBar);
            _state.Status.Should().Be(PrerequisiteStatus.Suspended);
            return await InfoBarAction();
        }

        public Task ShowReleaseSummaryAsync()
        {
            _calls.Add(ReleaseSummary);
            return Task.CompletedTask;
        }

        public Task HandleIncompatibleExtensionsAsync()
        {
            _calls.Add(IncompatibleExtensions);
            return Task.CompletedTask;
        }

        public Task InstallLatestAsync()
        {
            _calls.Add(Installer);
            return Task.CompletedTask;
        }

        public Task ShowUpdateNotificationAsync()
        {
            _calls.Add(UpdateNotification);
            return Task.CompletedTask;
        }
    }

    private sealed class HostVersionPrerequisiteProbe : IPrerequisiteProbe
    {
        private readonly Func<CancellationToken, Task<Version>> _getVisualStudioVersionAsync;

        public HostVersionPrerequisiteProbe(
            Func<CancellationToken, Task<Version>> getVisualStudioVersionAsync)
        {
            _getVisualStudioVersionAsync = getVisualStudioVersionAsync;
        }

        public int HostLookupCount { get; private set; }

        public int RunCount { get; private set; }

        public Task<Version> GetVisualStudioVersionAsync(CancellationToken cancellationToken)
        {
            HostLookupCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return _getVisualStudioVersionAsync(cancellationToken);
        }

        public string FindExecutable(string fileName)
        {
            return Path.Combine(@"C:\tools", fileName);
        }

        public Task<PrerequisiteCommandResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunCount++;
            var standardOutput = arguments.SequenceEqual(new[] { "default" })
                ? "stable-x86_64-pc-windows-msvc (default)"
                : string.Empty;
            return Task.FromResult(
                PrerequisiteCommandResult.Completed(0, standardOutput, string.Empty));
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(string Format, object[] Arguments)> Errors { get; } = new();

        public List<(string Format, object[] Arguments)> Lines { get; } = new();

        public void WriteLine(string format, params object[] args)
        {
            Lines.Add((format, args));
        }

        public void WriteError(string format, params object[] args)
        {
            Errors.Add((format, args));
        }
    }

    private sealed class RecordingTelemetry : ITelemetryService
    {
        public List<Exception> Exceptions { get; } = new();

        public void TrackEvent(string eventName, params (string Key, string Value)[] properties)
        {
        }

        public void TrackException(Exception e, string siteName = null)
        {
            Exceptions.Add(e);
        }

        public void TrackException(
            Exception e,
            (string Key, string Value)[] properties,
            string siteName = null)
        {
            Exceptions.Add(e);
        }
    }
}
