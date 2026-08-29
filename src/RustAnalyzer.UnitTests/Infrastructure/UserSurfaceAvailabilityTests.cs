using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using FluentAssertions;
using KS.RustAnalyzer.Debugger;
using KS.RustAnalyzer.Editor;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.LanguageService;
using KS.RustAnalyzer.NodeEnhancements;
using KS.RustAnalyzer.Shell;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

using ToolchainOperation = System.Func<KS.RustAnalyzer.TestAdapter.Common.IToolchainService, System.Func<KS.RustAnalyzer.TestAdapter.Common.BuildTargetInfo, KS.RustAnalyzer.TestAdapter.Common.BuildOutputSinks, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>>>;

[Trait("type", "UnitTests")]
public sealed class UserSurfaceAvailabilityTests
{
    [Fact]
    public void EveryRegisteredCommandUsesAReadinessGatedBase()
    {
        var expectedCommands = new[]
        {
            typeof(BuildAllCommand),
            typeof(CargoClippyCommand),
            typeof(CargoFmtCommand),
            typeof(CleanAllCommand),
            typeof(ClippyAll),
            typeof(FmtAllCommand),
            typeof(InstallToolchainCommand),
            typeof(KillOrphanedRaExesCommand),
            typeof(OptionsCommand),
            typeof(RestartLspCommand),
            typeof(RustToolbarCommand),
            typeof(RustToolbarMenuCommand),
            typeof(RustToolsMenuCommand),
            typeof(SwitchToolchainCommand),
            typeof(SwitchToolchainMenuCommand),
            typeof(TargetSystemComboCommand),
            typeof(TargetSystemComboGetListCommand),
        };
        var registeredCommands = typeof(RustAnalyzerPackage).Assembly
            .GetTypes()
            .Where(
                type => type.GetCustomAttribute<CommandAttribute>() != null)
            .ToArray();

        registeredCommands.Should().BeEquivalentTo(expectedCommands);
        registeredCommands.Should().OnlyContain(
            type => HasReadinessGatedBase(type));
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Suspended, false)]
    [InlineData(PrerequisiteStatus.Ready, true)]
    public async Task StaticCommandsHideAndRecheckBeforeExecutionAsync(
        PrerequisiteStatus status,
        bool expectedAvailable)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var telemetry = new RecordingTelemetry();
        var command = new TestRustCommand(fixture.State, telemetry);

        command.QueryStatus();
        command.Command.Visible.Should().Be(expectedAvailable);
        command.Command.Enabled.Should().Be(expectedAvailable);
        command.Command.Supported.Should().Be(expectedAvailable);
        command.ReadyStatusQueries.Should().Be(expectedAvailable ? 1 : 0);

        command.Invoke();
        command.Executions.Should().Be(expectedAvailable ? 1 : 0);
        telemetry.Events.Should().HaveCount(expectedAvailable ? 1 : 0);
    }

    [Fact]
    public async Task StaticCommandExecutionUsesStatusAfterQueryAsync()
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(
            PrerequisiteStatus.Evaluating);
        var command = new TestRustCommand(fixture.State, new RecordingTelemetry());

        command.QueryStatus();
        await fixture.CompleteEvaluationAsync(success: true);
        command.Invoke();

        command.Command.Visible.Should().BeFalse();
        command.Executions.Should().Be(1);
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Suspended, false)]
    [InlineData(PrerequisiteStatus.Ready, true)]
    public async Task ToolchainCommandMechanismsHideAndRecheckExecutionAsync(
        PrerequisiteStatus status,
        bool expectedAvailable)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var commands = new ICommandSurface[]
        {
            new TestToolchainCommand<CargoClippyCommand>(fixture.State),
            new TestBuildToolchainCommand(fixture.State),
        };

        foreach (var command in commands)
        {
            command.QueryStatus();
            await command.InvokeAsync();

            command.Command.Visible.Should().Be(expectedAvailable);
            command.Command.Enabled.Should().Be(expectedAvailable);
            command.Command.Supported.Should().Be(expectedAvailable);
            command.ReadyStatusQueries.Should().Be(expectedAvailable ? 1 : 0);
            command.Executions.Should().Be(expectedAvailable ? 1 : 0);
        }
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.NotEvaluated, true)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Evaluating, true)]
    public async Task CargoFileCommandsRestoreStatusWhenPendingStateBecomesReadyAsync(
        PrerequisiteStatus initialStatus,
        bool selectionAvailable)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(initialStatus);
        var commands = CreateCargoFileCommands(
            fixture.State,
            selectionAvailable);

        QueryAndAssertCommandStatus(
            commands,
            supported: false,
            visible: false,
            enabled: false,
            readyStatusQueries: 0);

        await fixture.TransitionToReadyAsync();

        QueryAndAssertCommandStatus(
            commands,
            supported: true,
            visible: selectionAvailable,
            enabled: selectionAvailable,
            readyStatusQueries: 1);
    }

    [Theory]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Failed, true)]
    [InlineData(PrerequisiteStatus.Suspended, false)]
    [InlineData(PrerequisiteStatus.Suspended, true)]
    public async Task CargoFileCommandsRestoreStatusForFreshReadyProcessAsync(
        PrerequisiteStatus unavailableStatus,
        bool selectionAvailable)
    {
        using var unavailableFixture =
            await PrerequisiteFixture.CreateAsync(unavailableStatus);
        var unavailableCommands = CreateCargoFileCommands(
            unavailableFixture.State,
            selectionAvailable);

        QueryAndAssertCommandStatus(
            unavailableCommands,
            supported: false,
            visible: false,
            enabled: false,
            readyStatusQueries: 0);

        using var readyFixture =
            await PrerequisiteFixture.CreateAsync(PrerequisiteStatus.Ready);
        var readyCommands = CreateCargoFileCommands(
            readyFixture.State,
            selectionAvailable,
            unavailableCommands
                .Select(command => command.Command)
                .ToArray());

        QueryAndAssertCommandStatus(
            readyCommands,
            supported: true,
            visible: selectionAvailable,
            enabled: selectionAvailable,
            readyStatusQueries: 1);
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, 0)]
    [InlineData(PrerequisiteStatus.Evaluating, 0)]
    [InlineData(PrerequisiteStatus.Failed, 0)]
    [InlineData(PrerequisiteStatus.Suspended, 0)]
    [InlineData(PrerequisiteStatus.Ready, 1)]
    public async Task TargetListReturnsNoChildrenUnlessReadyAsync(
        PrerequisiteStatus status,
        int expectedChildren)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);

        var targets = ExecuteTargetList(fixture.State);

        targets.Should().HaveCount(expectedChildren);
        if (status == PrerequisiteStatus.Ready)
        {
            targets.Should().Equal(TemporaryTargetSystemStore.TargetSystems);
        }
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Suspended, false)]
    [InlineData(PrerequisiteStatus.Ready, true)]
    public async Task EditorCommandsAreUnavailableAndInertUnlessReadyAsync(
        PrerequisiteStatus status,
        bool expectedAvailable)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var telemetry = new RecordingTelemetry();
        var comments = new List<bool>();
        var handler = new TestCommentSelectionCommandHandler(
            fixture.State,
            telemetry,
            (_, comment) =>
            {
                comments.Add(comment);
                return true;
            });
        var textView = Mock.Of<ITextView>();
        var textBuffer = Mock.Of<ITextBuffer>();
        var commentArgs = new CommentSelectionCommandArgs(textView, textBuffer);
        var uncommentArgs = new UncommentSelectionCommandArgs(textView, textBuffer);

        handler.GetCommandState(commentArgs).IsAvailable.Should().Be(expectedAvailable);
        handler.GetCommandState(uncommentArgs).IsAvailable.Should().Be(expectedAvailable);
        handler.ExecuteCommand(commentArgs, null).Should().Be(expectedAvailable);
        handler.ExecuteCommand(uncommentArgs, null).Should().Be(expectedAvailable);

        comments.Should().HaveCount(expectedAvailable ? 2 : 0);
        telemetry.Events.Should().HaveCount(expectedAvailable ? 2 : 0);
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Suspended, false)]
    [InlineData(PrerequisiteStatus.Ready, true)]
    public async Task OptionsPageIsReadOnlyAndDoesNotCreateStateUnlessReadyAsync(
        PrerequisiteStatus status,
        bool expectedAvailable)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        using var page = TestGeneralOptions.Create(
            fixture.State,
            fixture.Context.Factory);

        var propertyGrid = page.GetPropertyGrid();
        var automationObject = propertyGrid.SelectedObject;
        var properties = TypeDescriptor.GetProperties(automationObject)
            .Cast<PropertyDescriptor>()
            .Where(property => property.IsBrowsable)
            .ToArray();
        page.LoadSettingsFromStorage();
        page.LoadSettingsFromXml(null);
        page.SaveSettingsToStorage();
        page.SaveSettingsToXml(null);
        page.ResetSettings();

        properties.Should().NotBeEmpty();
        properties.Should().OnlyContain(
            property => property.IsReadOnly == !expectedAvailable);
        page.OptionsCreated.Should().Be(expectedAvailable ? 1 : 0);
        page.Loads.Should().Be(expectedAvailable ? 1 : 0);
        page.XmlLoads.Should().Be(expectedAvailable ? 1 : 0);
        page.Saves.Should().Be(expectedAvailable ? 1 : 0);
        page.XmlSaves.Should().Be(expectedAvailable ? 1 : 0);
        page.Resets.Should().Be(expectedAvailable ? 1 : 0);
        page.GetPropertyGrid().Should().BeSameAs(propertyGrid);
        page.AutomationObject.Should().BeSameAs(automationObject);
        if (expectedAvailable)
        {
            automationObject.Should().BeSameAs(page.Options);
        }
        else
        {
            automationObject.Should().NotBeOfType<Options>();
        }
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated)]
    [InlineData(PrerequisiteStatus.Evaluating)]
    public void CachedOptionsWindowSelectsLiveOptionsAfterReady(
        PrerequisiteStatus initialStatus)
    {
        using var fixture = PrerequisiteFixture.CreatePending(initialStatus);

        RunOptionsWindowTest(
            fixture,
            async () =>
            {
                using var page = TestGeneralOptions.Create(
                    fixture.State,
                    fixture.Context.Factory);
                var propertyGrid = page.GetPropertyGrid();

                propertyGrid.SelectedObject.Should().NotBeOfType<Options>();
                page.OptionsCreated.Should().Be(0);

                await fixture.TransitionToReadyAsync();
                await page.ReadinessObservation;

                propertyGrid.SelectedObject.Should().BeSameAs(page.Options);
                page.AutomationObject.Should().BeSameAs(page.Options);
                page.GetPropertyGrid().Should().BeSameAs(propertyGrid);
                page.OptionsCreated.Should().Be(1);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedOptionsWindowIgnoresLaterReady(
        bool disposePage)
    {
        using var fixture = PrerequisiteFixture.CreatePending(
            PrerequisiteStatus.Evaluating);

        RunOptionsWindowTest(
            fixture,
            async () =>
            {
                var page = TestGeneralOptions.Create(
                    fixture.State,
                    fixture.Context.Factory);
                var propertyGrid = page.GetPropertyGrid();
                var readinessObservation = page.ReadinessObservation;

                if (disposePage)
                {
                    page.Dispose();
                }
                else
                {
                    propertyGrid.Dispose();
                }

                await fixture.TransitionToReadyAsync();
                await readinessObservation;

                propertyGrid.IsDisposed.Should().Be(!disposePage);
                propertyGrid.SelectedObject.Should().NotBeSameAs(page.Options);
                page.OptionsCreated.Should().Be(0);
                page.Dispose();
                propertyGrid.Dispose();
            });
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated)]
    [InlineData(PrerequisiteStatus.Evaluating)]
    [InlineData(PrerequisiteStatus.Failed)]
    [InlineData(PrerequisiteStatus.Suspended)]
    public async Task OpenFolderQueriesReturnImmediatelyWithoutResolvingServicesAsync(
        PrerequisiteStatus status)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var toolchainResolutions = 0;
        var toolchain = new Lazy<IToolchainService>(
            () =>
            {
                toolchainResolutions++;
                return Mock.Of<IToolchainService>();
            });
        var workspace = new Mock<IWorkspace>(MockBehavior.Strict);
        var factory = new FileContextProviderFactory
        {
            AvailabilityPolicy = fixture.Policy,
            LazyCargoService = toolchain,
            L = fixture.Logger,
            OutputPane = Mock.Of<IBuildOutputSink>(),
            T = fixture.Telemetry,
        };
        var provider = factory.CreateProvider(workspace.Object);

        var contextsTask = provider.GetContextsForFileAsync(
            @"C:\workspace\Cargo.toml",
            default);

        contextsTask.IsCompleted.Should().BeTrue();
        (await contextsTask).Should().BeEmpty();
        toolchainResolutions.Should().Be(0);
        toolchain.IsValueCreated.Should().BeFalse();
        workspace.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpenFolderQueryDoesNotJoinEvaluationAndObservesLaterReadyStatusAsync()
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(
            PrerequisiteStatus.Evaluating);
        var metadataCalls = 0;
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(
                service => service.GetContainingPackageAsync(
                    It.IsAny<PathEx>(),
                    It.IsAny<CancellationToken>()))
            .Callback(() => metadataCalls++)
            .ReturnsAsync(
                (KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package)null);
        var provider = new FileContextProvider(
            () => metadata.Object,
            () => Mock.Of<IToolchainService>(),
            Mock.Of<IBuildOutputSink>(),
            () => throw new InvalidOperationException(
                "Settings should not be resolved."),
            fixture.Policy);

        var unavailableQuery = provider.GetContextsForFileAsync(
            @"C:\workspace\Cargo.toml",
            default);
        unavailableQuery.IsCompleted.Should().BeTrue();
        (await unavailableQuery).Should().BeEmpty();

        await fixture.CompleteEvaluationAsync(success: true);
        (await provider.GetContextsForFileAsync(
            @"C:\workspace\Cargo.toml",
            default)).Should().BeEmpty();

        metadataCalls.Should().Be(1);
        metadata.VerifyAll();
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated, false)]
    [InlineData(PrerequisiteStatus.Evaluating, false)]
    [InlineData(PrerequisiteStatus.Failed, false)]
    [InlineData(PrerequisiteStatus.Suspended, false)]
    [InlineData(PrerequisiteStatus.Ready, true)]
    public async Task OpenFolderExecutionIsInertUnlessReadyAsync(
        PrerequisiteStatus status,
        bool expectedAvailable)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var notifications = 0;
        var commands = 0;
        var context = new TestBuildFileContext(
            fixture.Policy,
            () =>
            {
                notifications++;
                return Task.CompletedTask;
            },
            (_, _, _) =>
            {
                commands++;
                return Task.FromResult(true);
            });

        var result = await context.ExecuteBuildAsync(
            Mock.Of<IBuildActionProgress>(),
            default);

        result.Should().Be(expectedAvailable);
        notifications.Should().Be(expectedAvailable ? 1 : 0);
        commands.Should().Be(expectedAvailable ? 1 : 0);
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated)]
    [InlineData(PrerequisiteStatus.Evaluating)]
    [InlineData(PrerequisiteStatus.Failed)]
    [InlineData(PrerequisiteStatus.Suspended)]
    public async Task NodeAndDebugSurfacesDoNotCreateOrInspectStateWhileUnavailableAsync(
        PrerequisiteStatus status)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var nodeProvider = new NodeBrowseObjectProvider(
            fixture.Telemetry,
            fixture.Logger,
            fixture.Policy);
        var debugProvider = new DebugLaunchTargetProvider
        {
            AvailabilityPolicy = fixture.Policy,
            L = fixture.Logger,
            T = fixture.Telemetry,
        };

        nodeProvider.ProvideBrowseObject(null).Should().BeNull();
        debugProvider.SupportsContext(null, null).Should().BeFalse();
        debugProvider.LaunchDebugTarget(null, null, null);

        GetBrowseObject(nodeProvider).Should().BeNull();
        fixture.Telemetry.Events.Should().BeEmpty();
        fixture.Telemetry.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task NodeSurfaceCreatesItsBackingObjectOnReadyPathAsync()
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(
            PrerequisiteStatus.Ready);
        var provider = new NodeBrowseObjectProvider(
            fixture.Telemetry,
            fixture.Logger,
            fixture.Policy);

        Action provide = () => provider.ProvideBrowseObject(null);

        provide.Should().Throw<NullReferenceException>();
        GetBrowseObject(provider).Should().NotBeNull();
    }

    [Fact]
    public async Task DebugSurfaceQueriesMetadataOnReadyPathAsync()
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(
            PrerequisiteStatus.Ready);
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(
                service => service.GetContainingPackageAsync(
                    It.IsAny<PathEx>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (KS.RustAnalyzer.TestAdapter.Cargo.Workspace.Package)null);
        var workspace = new Mock<IWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(value => value.JTF).Returns(fixture.Context.Factory);
        workspace.Setup(value => value.GetService(typeof(IMetadataService)))
            .Returns(metadata.Object);
        var provider = new DebugLaunchTargetProvider
        {
            AvailabilityPolicy = fixture.Policy,
            L = fixture.Logger,
            T = fixture.Telemetry,
        };

        provider.SupportsContext(
            workspace.Object,
            @"C:\workspace\src\main.rs").Should().BeFalse();

        metadata.VerifyAll();
    }

    [Theory]
    [InlineData(PrerequisiteStatus.NotEvaluated)]
    [InlineData(PrerequisiteStatus.Evaluating)]
    [InlineData(PrerequisiteStatus.Failed)]
    [InlineData(PrerequisiteStatus.Suspended)]
    public async Task TestExplorerSurfaceHasNoContainersOrWorkspaceSubscriptionsAsync(
        PrerequisiteStatus status)
    {
        using var fixture = await PrerequisiteFixture.CreateAsync(status);
        var workspaceLookups = 0;
        using var discoverer = new TestContainerDiscoverer(
            () =>
            {
                workspaceLookups++;
                return Mock.Of<Microsoft.VisualStudio.Workspace.VSIntegration.Contracts.IVsFolderWorkspaceService>();
            },
            new TL { L = fixture.Logger, T = fixture.Telemetry },
            fixture.Policy,
            fixture.Context.Factory);

        discoverer.TestContainers.Should().BeEmpty();
        workspaceLookups.Should().Be(0);

        discoverer.Dispose();
        await discoverer.Initialization;
        workspaceLookups.Should().Be(0);
    }

    private static NodeBrowseObjectPropertyFilter<NodeBrowseObject> GetBrowseObject(
        NodeBrowseObjectProvider provider)
    {
        return typeof(NodeBrowseObjectProvider)
            .GetField(
                "_browseObject",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(provider) as NodeBrowseObjectPropertyFilter<NodeBrowseObject>;
    }

    private static ICommandSurface[] CreateCargoFileCommands(
        PrerequisiteProcessState prerequisiteState,
        bool selectionAvailable,
        OleMenuCommand[] commands = null)
    {
        return new ICommandSurface[]
        {
            new TestToolchainCommand<CargoClippyCommand>(
                prerequisiteState,
                selectionAvailable,
                commands?[0]),
            new TestToolchainCommand<CargoFmtCommand>(
                prerequisiteState,
                selectionAvailable,
                commands?[1]),
        };
    }

    private static void QueryAndAssertCommandStatus(
        ICommandSurface[] commands,
        bool supported,
        bool visible,
        bool enabled,
        int readyStatusQueries)
    {
        foreach (var command in commands)
        {
            command.QueryStatus();
            command.Command.Supported.Should().Be(supported);
            command.Command.Visible.Should().Be(visible);
            command.Command.Enabled.Should().Be(enabled);
            command.ReadyStatusQueries.Should().Be(readyStatusQueries);
        }
    }

    private static bool HasReadinessGatedBase(Type type)
    {
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType)
            {
                var definition = baseType.GetGenericTypeDefinition();
                if (definition == typeof(BaseRustAnalyzerCommand<>) ||
                    definition == typeof(BaseToolchainCommand<>) ||
                    definition == typeof(BaseBuildToolChainCommand<>))
                {
                    return true;
                }
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static void RunOptionsWindowTest(
        PrerequisiteFixture fixture,
        Func<Task> test)
    {
        var synchronizationContext = SynchronizationContext.Current;
        try
        {
            var testContext = fixture.MainThreadSynchronizationContext;
            SynchronizationContext.SetSynchronizationContext(testContext);
            var operation = fixture.Context.Factory.RunAsync(test);
            var frame = new SingleThreadedSynchronizationContext.Frame();
            _ = operation.Task.ContinueWith(
                _ => testContext.Post(
                    _ => frame.Continue = false,
                    null),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            testContext.PushFrame(frame);
            operation.Join();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(
                synchronizationContext);
        }
    }

    private static string[] ExecuteTargetList(PrerequisiteProcessState state)
    {
        var constructor = typeof(TargetSystemComboGetListCommand).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(PrerequisiteProcessState), },
            null);
        constructor.Should().NotBeNull();
        var command = (TargetSystemComboGetListCommand)constructor.Invoke(
            new object[] { state, });
        SetTelemetry(command, new RecordingTelemetry());
        const BindingFlags executeBindingFlags =
            BindingFlags.DeclaredOnly |
            BindingFlags.Instance |
            BindingFlags.NonPublic;
        var execute = typeof(BaseRustAnalyzerCommand<TargetSystemComboGetListCommand>)
            .GetMethod("Execute", executeBindingFlags);
        execute.Should().NotBeNull();
        var variant = Marshal.AllocCoTaskMem(16);
        for (var index = 0; index < 16; index++)
        {
            Marshal.WriteByte(variant, index, 0);
        }

        try
        {
            execute.Invoke(
                command,
                new object[] { command, new OleMenuCmdEventArgs(null, variant), });
            return (string[])Marshal.GetObjectForNativeVariant(variant);
        }
        finally
        {
            _ = VariantClear(variant);
            Marshal.FreeCoTaskMem(variant);
        }
    }

    private static void SetTelemetry<T>(
        BaseRustAnalyzerCommand<T> command,
        ITelemetryService telemetry)
        where T : class, new()
    {
        typeof(BaseRustAnalyzerCommand<T>)
            .GetField(
                "_telemetry",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(command, telemetry);
    }

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(IntPtr variant);

    private interface ICommandSurface
    {
        OleMenuCommand Command { get; }

        int Executions { get; }

        int ReadyStatusQueries { get; }

        Task InvokeAsync();

        void QueryStatus();
    }

    private sealed class TestRustCommand : BaseRustAnalyzerCommand<TestRustCommand>
    {
        public TestRustCommand()
            : this(PrerequisiteProcessState.Current, new RecordingTelemetry())
        {
        }

        public TestRustCommand(
            PrerequisiteProcessState prerequisiteState,
            ITelemetryService telemetry)
            : base(prerequisiteState)
        {
            Command = CreateMenuCommand();
            SetTelemetry(this, telemetry);
        }

        public int Executions { get; private set; }

        public int ReadyStatusQueries { get; private set; }

        public void Invoke()
        {
            Execute(this, EventArgs.Empty);
        }

        public void QueryStatus()
        {
            BeforeQueryStatus(EventArgs.Empty);
        }

        protected override void BeforeQueryStatusReady(EventArgs e)
        {
            ReadyStatusQueries++;
            Command.Visible = Command.Enabled = Command.Supported = true;
        }

        protected override void ExecuteCore(
            object sender,
            OleMenuCmdEventArgs eventArgs)
        {
            Executions++;
        }
    }

    private sealed class TestToolchainCommand<TCommand> :
        BaseToolchainCommand<TCommand>,
        ICommandSurface
        where TCommand : class, new()
    {
        private readonly bool _selectionAvailable;

        public TestToolchainCommand(
            PrerequisiteProcessState prerequisiteState,
            bool selectionAvailable = true,
            OleMenuCommand command = null)
            : base(prerequisiteState)
        {
            _selectionAvailable = selectionAvailable;
            Command = command ?? CreateMenuCommand();
        }

        public int Executions { get; private set; }

        public int ReadyStatusQueries { get; private set; }

        protected override ToolchainOperation Operation =>
            throw new NotSupportedException();

        public Task InvokeAsync()
        {
            return ExecuteAsync(null);
        }

        public void QueryStatus()
        {
            BeforeQueryStatus(EventArgs.Empty);
        }

        protected override void BeforeQueryStatusReady(EventArgs e)
        {
            ReadyStatusQueries++;
            Command.Visible = Command.Enabled = _selectionAvailable;
        }

        protected override Task ExecuteReadyAsync(OleMenuCmdEventArgs e)
        {
            Executions++;
            return Task.CompletedTask;
        }

        protected override string GetOptions(Options opts)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestBuildToolchainCommand :
        BaseBuildToolChainCommand<TestBuildToolchainCommand>,
        ICommandSurface
    {
        public TestBuildToolchainCommand()
            : this(PrerequisiteProcessState.Current)
        {
        }

        public TestBuildToolchainCommand(
            PrerequisiteProcessState prerequisiteState)
            : base(prerequisiteState)
        {
            Command = CreateMenuCommand();
        }

        public int Executions { get; private set; }

        public int ReadyStatusQueries { get; private set; }

        protected override ToolchainOperation Operation =>
            throw new NotSupportedException();

        public Task InvokeAsync()
        {
            return ExecuteAsync(null);
        }

        public void QueryStatus()
        {
            BeforeQueryStatus(EventArgs.Empty);
        }

        protected override void BeforeQueryStatusReady(EventArgs e)
        {
            ReadyStatusQueries++;
            Command.Visible = Command.Enabled = Command.Supported = true;
        }

        protected override Task ExecuteReadyAsync(OleMenuCmdEventArgs e)
        {
            Executions++;
            return Task.CompletedTask;
        }

        protected override string GetOptions(Options opts)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestCommentSelectionCommandHandler :
        CommentSelectionCommandHandler
    {
        public TestCommentSelectionCommandHandler(
            PrerequisiteProcessState prerequisiteState,
            ITelemetryService telemetry,
            Func<ITextView, bool, bool> changeComment)
            : base(
                telemetry,
                Mock.Of<ILogger>(),
                prerequisiteState,
                changeComment)
        {
        }
    }

    [ComVisible(true)]
    private sealed class TestGeneralOptions : OptionsProvider.GeneralOptions
    {
        private Options _options;

        public int Loads { get; private set; }

        public Options Options => _options;

        public int OptionsCreated { get; private set; }

        public Task ReadinessObservation
        {
            get
            {
                var observation = typeof(OptionsProvider.GeneralOptions)
                    .GetField(
                        "_readinessObservation",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(this) as JoinableTask;
                return observation?.Task ?? Task.CompletedTask;
            }
        }

        public int Resets { get; private set; }

        public int Saves { get; private set; }

        public int XmlLoads { get; private set; }

        public int XmlSaves { get; private set; }

        public static TestGeneralOptions Create(
            PrerequisiteProcessState prerequisiteState,
            JoinableTaskFactory joinableTaskFactory)
        {
            var page = (TestGeneralOptions)FormatterServices
                .GetUninitializedObject(typeof(TestGeneralOptions));
            SetField(
                typeof(OptionsProvider.GeneralOptions),
                page,
                "_prerequisiteState",
                prerequisiteState);
            SetField(
                typeof(OptionsProvider.GeneralOptions),
                page,
                "_joinableTaskFactory",
                joinableTaskFactory);
            SetField(
                typeof(OptionsProvider.GeneralOptions),
                page,
                "_lifetimeCancellation",
                new CancellationTokenSource());
            SetField(
                typeof(DialogPage),
                page,
                "usxChangeSubscriptionsByMoniker",
                new Dictionary<string, IDisposable>());
            SetField(
                typeof(DialogPage),
                page,
                "settingsManager2Lazy",
                new AsyncLazy<ISettingsManager2>(
                    () => Task.FromResult<ISettingsManager2>(null),
                    joinableTaskFactory));
            page._options = new Options();
            return page;
        }

        public PropertyGrid GetPropertyGrid()
        {
            return (PropertyGrid)Window;
        }

        protected override Options CreateOptions()
        {
            OptionsCreated++;
            return Options;
        }

        protected override void LoadOptions(Options options)
        {
            options.Should().BeSameAs(Options);
            Loads++;
        }

        protected override void ResetOptions()
        {
            Resets++;
        }

        protected override void LoadOptionsFromXml(IVsSettingsReader reader)
        {
            XmlLoads++;
        }

        protected override void SaveOptions(Options options)
        {
            options.Should().BeSameAs(Options);
            Saves++;
        }

        protected override void SaveOptionsToXml(IVsSettingsWriter writer)
        {
            XmlSaves++;
        }

        private static void SetField(
            Type declaringType,
            object target,
            string fieldName,
            object value)
        {
            declaringType.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }

    private sealed class TestBuildFileContext : BuildFileContextBase
    {
        public TestBuildFileContext(
            PrerequisiteAvailabilityPolicy availabilityPolicy,
            Func<Task> showUpdateNotificationAsync,
            Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>>
                command)
            : base(
                new BuildTargetInfo(),
                Mock.Of<IBuildOutputSink>(),
                command,
                availabilityPolicy,
                AutomaticRustPath.OpenFolderBuild,
                showUpdateNotificationAsync)
        {
        }
    }

    private sealed class PrerequisiteFixture : IDisposable
    {
        private Task<PrerequisiteResult> _evaluation;
        private TaskCompletionSource<PrerequisiteResult> _evaluationCompletion;

        private PrerequisiteFixture(bool isolateMainThread = false)
        {
            if (isolateMainThread)
            {
                MainThreadSynchronizationContext =
                    new SingleThreadedSynchronizationContext();
                Context = new JoinableTaskContext(
                    Thread.CurrentThread,
                    MainThreadSynchronizationContext);
            }
            else
            {
                Context = new JoinableTaskContext();
            }

            State = new PrerequisiteProcessState(Context.Factory);
            Logger = Mock.Of<ILogger>();
            Telemetry = new RecordingTelemetry();
            Policy = new PrerequisiteAvailabilityPolicy(
                State,
                Logger,
                Telemetry);
        }

        public JoinableTaskContext Context { get; }

        public ILogger Logger { get; }

        public SingleThreadedSynchronizationContext MainThreadSynchronizationContext { get; }

        public PrerequisiteAvailabilityPolicy Policy { get; }

        public PrerequisiteProcessState State { get; }

        public RecordingTelemetry Telemetry { get; }

        public static async Task<PrerequisiteFixture> CreateAsync(
            PrerequisiteStatus status)
        {
            EnsureArg.IsTrue(
                Enum.IsDefined(typeof(PrerequisiteStatus), status),
                nameof(status),
                options => options.WithException(
                    new ArgumentOutOfRangeException(nameof(status))));
            var fixture = new PrerequisiteFixture();
            switch (status)
            {
                case PrerequisiteStatus.NotEvaluated:
                    break;
                case PrerequisiteStatus.Evaluating:
                    fixture._evaluationCompletion =
                        new TaskCompletionSource<PrerequisiteResult>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                    fixture._evaluation = fixture.State.GetOrEvaluateAsync(
                        _ => fixture._evaluationCompletion.Task,
                        default);
                    break;
                case PrerequisiteStatus.Ready:
                    await fixture.State.GetOrEvaluateAsync(
                        _ => Task.FromResult(PrerequisiteResult.Success),
                        default);
                    break;
                case PrerequisiteStatus.Failed:
                case PrerequisiteStatus.Suspended:
                    await fixture.State.GetOrEvaluateAsync(
                        _ => Task.FromResult(FailedResult),
                        default);
                    if (status == PrerequisiteStatus.Suspended)
                    {
                        fixture.State.Suspend();
                    }

                    break;
                default:
                    throw new InvalidOperationException();
            }

            fixture.State.Status.Should().Be(status);
            return fixture;
        }

        public static PrerequisiteFixture CreatePending(
            PrerequisiteStatus status)
        {
            EnsureArg.IsTrue(
                status == PrerequisiteStatus.NotEvaluated ||
                    status == PrerequisiteStatus.Evaluating,
                nameof(status),
                options => options.WithException(
                    new ArgumentOutOfRangeException(nameof(status))));
            var fixture = new PrerequisiteFixture(isolateMainThread: true);
            if (status == PrerequisiteStatus.Evaluating)
            {
                fixture._evaluationCompletion =
                    new TaskCompletionSource<PrerequisiteResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                fixture._evaluation = fixture.State.GetOrEvaluateAsync(
                    _ => fixture._evaluationCompletion.Task,
                    default);
            }

            fixture.State.Status.Should().Be(status);
            return fixture;
        }

        public async Task CompleteEvaluationAsync(
            bool success,
            bool suspend = false)
        {
            Assert.NotNull(_evaluationCompletion);
            Assert.NotNull(_evaluation);
            _evaluationCompletion.SetResult(
                success ? PrerequisiteResult.Success : FailedResult);
            await _evaluation;
            if (suspend)
            {
                State.Suspend();
            }
        }

        public async Task TransitionToReadyAsync()
        {
            if (_evaluationCompletion == null)
            {
                await State.GetOrEvaluateAsync(
                    _ => Task.FromResult(PrerequisiteResult.Success),
                    default);
                return;
            }

            await CompleteEvaluationAsync(success: true);
        }

        public void Dispose()
        {
            if (_evaluationCompletion?.TrySetResult(FailedResult) == true)
            {
                _evaluation.GetAwaiter().GetResult();
            }

            Context.Dispose();
        }

        private static PrerequisiteResult FailedResult =>
            PrerequisiteResult.Failed(
                new[]
                {
                    new PrerequisiteFailure(
                        PrerequisiteFailureKind.CargoNotFound,
                        "Cargo was not found."),
                });
    }

    private sealed class RecordingTelemetry : ITelemetryService
    {
        public List<string> Events { get; } = new();

        public List<Exception> Exceptions { get; } = new();

        public void TrackEvent(
            string eventName,
            params (string Key, string Value)[] properties)
        {
            Events.Add(eventName);
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

    private static OleMenuCommand CreateMenuCommand()
    {
        return new OleMenuCommand(
            (_, _) => { },
            new CommandID(Guid.NewGuid(), 1));
    }
}
