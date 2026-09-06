using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Debugger;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.LanguageService;
using KS.RustAnalyzer.Shell;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class FeatureUsageBoundaryTests
{
    [Theory]
    [InlineData(UsageOperation.LaunchDebug, UsageOutcome.Succeeded)]
    [InlineData(UsageOperation.LaunchDebug, UsageOutcome.Failed)]
    [InlineData(UsageOperation.LaunchDebug, UsageOutcome.Cancelled)]
    [InlineData(UsageOperation.LaunchRun, UsageOutcome.Succeeded)]
    [InlineData(UsageOperation.LaunchRun, UsageOutcome.Failed)]
    [InlineData(UsageOperation.LaunchRun, UsageOutcome.Cancelled)]
    public async Task LaunchReportsOneTerminalOutcomeAsync(
        UsageOperation operation,
        UsageOutcome outcome)
    {
        var telemetry = new RecordingFeatureUsageTelemetry();
        var provider = new DebugLaunchTargetProvider
        {
            UsageTelemetry = telemetry,
        };
        var method = typeof(DebugLaunchTargetProvider).GetMethod(
            "TrackLaunchAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        Func<Task<bool>> launch = outcome switch
        {
            UsageOutcome.Succeeded => () => Task.FromResult(true),
            UsageOutcome.Failed => () => Task.FromResult(false),
            UsageOutcome.Cancelled => () => Task.FromCanceled<bool>(new CancellationToken(true)),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        var tracked = (Task)method.Invoke(
            provider,
            new object[] { operation, launch });

        if (outcome == UsageOutcome.Cancelled)
        {
            await ((Func<Task>)(async () => await tracked))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await tracked;
        }

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == operation && value.Outcome == outcome);
    }

    [Theory]
    [InlineData(false, UsageOperation.LaunchDebug)]
    [InlineData(true, UsageOperation.LaunchRun)]
    public async Task LaunchBoundarySelectsDebugOrRunAsync(
        bool noDebug,
        UsageOperation expectedOperation)
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        var telemetry = new RecordingFeatureUsageTelemetry();
        var expected = new InvalidOperationException();
        var metadata = new Mock<IMetadataService>();
        metadata.Setup(service => service.GetContainingPackageAsync(
                It.IsAny<PathEx>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var workspace = new Mock<IWorkspace>();
        workspace.Setup(value => value.GetService(typeof(IMetadataService)))
            .Returns(metadata.Object);
        var configuration = new Mock<IPropertySettings>();
        configuration.Setup(value => value.ContainsKey(LaunchConfigurationConstants.NoDebugKey))
            .Returns(noDebug);
        configuration.Setup(value => value.ContainsKey(LaunchConfigurationConstants.ProgramKey))
            .Returns(true);
        configuration.Setup(value => value[LaunchConfigurationConstants.ProgramKey])
            .Returns("program");
        var provider = new DebugLaunchTargetProvider
        {
            AvailabilityPolicy = new PrerequisiteAvailabilityPolicy(
                state,
                Mock.Of<ILogger>()),
            L = Mock.Of<ILogger>(),
            UsageTelemetry = telemetry,
        };
        var method = typeof(DebugLaunchTargetProvider).GetMethod(
            "LaunchDebugTargetAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var launch = (Task)method.Invoke(
            provider,
            new object[]
            {
                workspace.Object,
                null,
                new DebugLaunchTargetProvider.LaunchConfigWrapper(
                configuration.Object,
                Mock.Of<ILogger>()),
                CancellationToken.None,
            });

        (await ((Func<Task>)(async () => await launch))
                .Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == expectedOperation
                && value.Outcome == UsageOutcome.Failed);
    }

    [Theory]
    [InlineData(UsageOperation.ToolchainInstall, UsageOutcome.Succeeded)]
    [InlineData(UsageOperation.ToolchainInstall, UsageOutcome.Failed)]
    [InlineData(UsageOperation.ToolchainInstall, UsageOutcome.Cancelled)]
    [InlineData(UsageOperation.ToolchainSwitch, UsageOutcome.Succeeded)]
    [InlineData(UsageOperation.ToolchainSwitch, UsageOutcome.Failed)]
    [InlineData(UsageOperation.ToolchainSwitch, UsageOutcome.Cancelled)]
    public async Task ToolchainActionsReportOneTerminalOutcomeAsync(
        UsageOperation operation,
        UsageOutcome outcome)
    {
        var telemetry = new RecordingFeatureUsageTelemetry();

        await TestRustCommand.TrackToolchainUsageAsync(
            telemetry,
            operation,
            () => Task.FromResult(outcome));

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == operation && value.Outcome == outcome);
    }

    [Fact]
    public async Task DismissedToolchainInstallerReportsNoUsageAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        var telemetry = new RecordingFeatureUsageTelemetry();
        Func<Task<(bool Accepted, string CommandLine, string ToolchainName)>> selectInstallation =
            () => Task.FromResult((false, (string)null, (string)null));
        var constructor = typeof(InstallToolchainCommand).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(PrerequisiteProcessState),
                typeof(Func<Task<(bool Accepted, string CommandLine, string ToolchainName)>>),
            },
            null);
        constructor.Should().NotBeNull();
        var command = (InstallToolchainCommand)constructor.Invoke(
            new object[] { state, selectInstallation });
        SetUsageTelemetry(command, telemetry);
        var executeReady = typeof(InstallToolchainCommand).GetMethod(
            "ExecuteReadyAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        executeReady.Should().NotBeNull();

        await (Task)executeReady.Invoke(command, Array.Empty<object>());

        telemetry.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidOrUnavailableToolchainSwitchReportsNoUsageAsync(
        bool available,
        bool solutionAvailable)
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        if (available)
        {
            await state.GetOrEvaluateAsync(
                _ => Task.FromResult(PrerequisiteResult.Success),
                default);
        }

        var policy = new PrerequisiteAvailabilityPolicy(state, Mock.Of<ILogger>());
        var constructor = typeof(SwitchToolchainCommand).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(PrerequisiteProcessState),
                typeof(PrerequisiteAvailabilityPolicy),
                typeof(Action),
            },
            null);
        constructor.Should().NotBeNull();
        var command = (SwitchToolchainCommand)constructor.Invoke(
            new object[] { state, policy, (Action)(() => { }) });
        var telemetry = new RecordingFeatureUsageTelemetry();
        SetUsageTelemetry(command, telemetry);
        var solution = new Mock<IVsSolution>();
        string solutionDirectory = @"C:\workspace";
        string solutionFile = null;
        string userOptionsFile = null;
        solution.Setup(value => value.GetSolutionInfo(
                out solutionDirectory,
                out solutionFile,
                out userOptionsFile))
            .Returns(solutionAvailable ? VSConstants.S_OK : VSConstants.E_FAIL);
        SetBaseField(command, "_solution", solution.Object);
        var execute = typeof(SwitchToolchainCommand).GetMethod(
            "ExecuteCore",
            BindingFlags.Instance | BindingFlags.NonPublic);
        execute.Should().NotBeNull();

        execute.Invoke(command, new object[] { command.Command, null });

        telemetry.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, UsageOutcome.Failed)]
    [InlineData(true, UsageOutcome.Cancelled)]
    public async Task LanguageServerActivationReportsOneTerminalFailureAsync(
        bool cancelled,
        UsageOutcome expectedOutcome)
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(PrerequisiteResult.Success),
            default);
        var telemetry = new RecordingFeatureUsageTelemetry();
        var exception = cancelled
            ? new OperationCanceledException()
            : (Exception)new InvalidOperationException();
        var downloader = new Mock<IRlsInstallerService>(MockBehavior.Strict);
        downloader.Setup(service => service.GetExePathAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<PathEx>(exception));
        using var client = new LanguageClient(context.Factory)
        {
            AvailabilityPolicy = new PrerequisiteAvailabilityPolicy(
                state,
                Mock.Of<ILogger>()),
            RADownloader = downloader.Object,
            UsageTelemetry = telemetry,
        };
        Func<Task> activate = async () => await client.ActivateAsync(default);

        await activate.Should().ThrowAsync<Exception>();

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.LanguageServerActivate
                    && value.Outcome == expectedOutcome);
    }

    [Fact]
    public async Task LanguageServerInitializationReportsSuccessOnceAsync()
    {
        using var context = new JoinableTaskContext();
        var telemetry = new RecordingFeatureUsageTelemetry();
        using var client = new LanguageClient(context.Factory)
        {
            UsageTelemetry = telemetry,
        };
        var beginActivation = typeof(LanguageClient).GetMethod(
            "BeginActivation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        beginActivation.Should().NotBeNull();
        beginActivation.Invoke(client, Array.Empty<object>());

        await client.OnServerInitializedAsync();
        await client.OnServerInitializedAsync();

        telemetry.Events.Should().ContainSingle()
            .Which.Should().Match<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)>(
                value => value.Operation == UsageOperation.LanguageServerActivate
                    && value.Outcome == UsageOutcome.Succeeded);
    }

    private static void SetUsageTelemetry<T>(
        BaseRustAnalyzerCommand<T> command,
        IFeatureUsageTelemetry telemetry)
        where T : class, new()
    {
        SetBaseField(command, "_usageTelemetry", telemetry);
    }

    private static void SetBaseField<T>(
        BaseRustAnalyzerCommand<T> command,
        string name,
        object value)
        where T : class, new()
    {
        typeof(BaseRustAnalyzerCommand<T>)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(command, value);
    }

    private sealed class TestRustCommand : BaseRustAnalyzerCommand<TestRustCommand>
    {
        public static Task TrackToolchainUsageAsync(
            IFeatureUsageTelemetry telemetry,
            UsageOperation operation,
            Func<Task<UsageOutcome>> execute)
        {
            return BaseRustAnalyzerCommand<TestRustCommand>.TrackUsageAsync(
                telemetry,
                operation,
                execute);
        }

        protected override void ExecuteCore(
            object sender,
            Microsoft.VisualStudio.Shell.OleMenuCmdEventArgs eventArgs)
        {
        }
    }
}
