using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Debugger;
using KS.RustAnalyzer.Editor;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.NodeEnhancements;
using KS.RustAnalyzer.Shell;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.VSIntegration.UI;
using Moq;
using Xunit;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;
using WorkspaceModel = KS.RustAnalyzer.TestAdapter.Cargo.Workspace;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class UserSurfaceLoggingTests
{
    [Fact]
    public async Task PackageCompositionPropagatesOwnerCategoriesAsync()
    {
        using var context = new JoinableTaskContext();
        var jtf = typeof(RustAnalyzerPackage).GetField(
            "_jtf",
            BindingFlags.Static | BindingFlags.NonPublic);
        jtf.Should().NotBeNull();
        var originalJtf = jtf.GetValue(null);
        jtf.SetValue(null, context.Factory);
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        try
        {
            var package = CreatePackage(context);
            SetProperty(
                package,
                typeof(AsyncPackage),
                "JoinableTaskFactory",
                context.Factory);
            var registry = new Mock<IRegistrySettingsService>();
            registry.SetupGet(value => value.InfoBarDismissedByUser)
                .Returns(true);
            var componentModel = new Mock<IComponentModel>();
            componentModel.Setup(value => value.GetService<ILoggerFactory>())
                .Returns(factory);
            componentModel
                .Setup(value => value.GetService<IRegistrySettingsService>())
                .Returns(registry.Object);
            componentModel
                .Setup(value =>
                    value.GetService<PrerequisiteAvailabilityPolicy>())
                .Returns(
                    new PrerequisiteAvailabilityPolicy(
                        new PrerequisiteProcessState(context.Factory),
                        Mock.Of<LegacyLogger>()));
            AddPackageService(
                package,
                typeof(SComponentModel),
                componentModel.Object);
            AddPackageService(
                package,
                typeof(IMenuCommandService),
                Mock.Of<IMenuCommandService>());
            var initialize = typeof(RustAnalyzerPackage).GetMethod(
                "InitializeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            initialize.Should().NotBeNull();

            await (Task)initialize.Invoke(
                package,
                new object[] { CancellationToken.None, null, });

            var startup = (IPrerequisiteStartupOperations)package;
            await startup.HandleIncompatibleExtensionsAsync();
            await startup.ShowReleaseSummaryAsync();

            var entries = provider.Entries.ToArray();
            entries.Should().HaveCount(4);
            AssertEntry(
                entries[0],
                typeof(RustAnalyzerPackage),
                LogLevel.Information,
                new EventId(1, "IncompatibleExtensionsSearchStarted"),
                "Searching and disabling incompatible extensions.");
            AssertEntry(
                entries[1],
                typeof(RustAnalyzerPackage),
                LogLevel.Information,
                new EventId(2, "IncompatibleExtensionsSearchFailed"),
                "Failed in searching and disabling incompatible extensions.");
            entries[1].Exception.Should().NotBeNull();
            AssertEntry(
                entries[2],
                typeof(RustAnalyzerPackage.ReleaseSummaryNotification),
                LogLevel.Information,
                new EventId(1, "ReleaseNotesShowAttempted"),
                "Attempting to show release notes...");
            AssertEntry(
                entries[3],
                typeof(RustAnalyzerPackage.ReleaseSummaryNotification),
                LogLevel.Information,
                new EventId(2, "ReleaseNotesAlreadyDismissed"),
                "... Not showing release notes as it has already been dismissed by the user.");
            entries.Where(entry => entry != entries[1])
                .Should()
                .OnlyContain(entry => entry.Exception == null);
            registry.VerifyGet(
                value => value.InfoBarDismissedByUser,
                Times.Once);
        }
        finally
        {
            jtf.SetValue(null, originalJtf);
        }
    }

    [Fact]
    public async Task EditorFactoriesLogTheirActualOwnerCategoriesAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var workspace = Mock.Of<IWorkspace>();
        var contextFactory = new FileContextProviderFactory
        {
            AvailabilityPolicy = readiness.Policy,
            L = Mock.Of<LegacyLogger>(),
            LazyCargoService = new Lazy<IToolchainService>(
                () => Mock.Of<IToolchainService>()),
            LoggerFactory = factory,
            OutputPane = Mock.Of<IBuildOutputSink>(),
        };
        var scannerFactory = new FileScannerFactory
        {
            AvailabilityPolicy = readiness.Policy,
            L = Mock.Of<LegacyLogger>(),
            LoggerFactory = factory,
        };

        contextFactory.CreateProvider(workspace);
        scannerFactory.CreateProvider(workspace);

        var entries = provider.Entries.ToArray();
        entries.Should().HaveCount(2);
        AssertEntry(
            entries[0],
            typeof(FileContextProviderFactory),
            LogLevel.Information,
            new EventId(1, "ProviderCreated"),
            "Creating {ProviderType}.");
        entries[0].Properties["ProviderType"].Should().Be(
            nameof(FileContextProviderFactory));
        AssertEntry(
            entries[1],
            typeof(FileScannerFactory),
            LogLevel.Information,
            new EventId(1, "ProviderCreated"),
            "Creating {ProviderType}.");
        entries[1].Properties["ProviderType"].Should().Be(
            nameof(FileScannerFactory));
        entries.Should().OnlyContain(entry => entry.Exception == null);
    }

    [Fact]
    public async Task NodeLogsRequestAndAsyncUpdateFailureAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var nodeProvider = new NodeBrowseObjectProvider(
            factory,
            readiness.Policy);
        var container = new Mock<INodeContainer>();
        container.SetupGet(value => value.JTF)
            .Returns(readiness.Context.Factory);
        container.SetupGet(value => value.NodeExtenders)
            .Returns(Array.Empty<INodeExtender>());
        container.SetupGet(value => value.WorkspaceClosing)
            .Returns(new CancellationTokenSource());
        var node = new Mock<WorkspaceVisualNodeBase>(
            MockBehavior.Loose,
            container.Object);
        typeof(WorkspaceVisualNodeBase).GetField(
                "fullMoniker",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(node.Object, @"C:\workspace\src\main.rs");

        nodeProvider.ProvideBrowseObject(node.Object).Should().BeNull();
        var browseObjectField = typeof(NodeBrowseObjectProvider).GetField(
            "_browseObject",
            BindingFlags.Instance | BindingFlags.NonPublic);
        browseObjectField.Should().NotBeNull();
        var browseObject = browseObjectField.GetValue(nodeProvider)
            as NodeBrowseObjectPropertyFilter<NodeBrowseObject>;
        browseObject.Should().NotBeNull();
        var jtf = typeof(RustAnalyzerPackage).GetField(
            "_jtf",
            BindingFlags.Static | BindingFlags.NonPublic);
        jtf.Should().NotBeNull();
        var originalJtf = jtf.GetValue(null);
        var cancelled = new CancellationToken(true);
        var settings = new Mock<ISettingsService>();
        settings.Setup(value => value.SetAsync(
                SettingsInfo.TypeDebuggerEnvironment,
                It.IsAny<PathEx>(),
                "cancelled"))
            .Returns(Task.FromCanceled(cancelled));
        try
        {
            jtf.SetValue(null, readiness.Context.Factory);
            browseObject.Object.CommandLineArguments = "fault";
            browseObject.Object.SS = settings.Object;
            browseObject.Object.DebuggerEnvironment = "cancelled";
        }
        finally
        {
            jtf.SetValue(null, originalJtf);
        }

        var entries = provider.Entries.ToArray();
        entries.Should().HaveCount(2);
        AssertEntry(
            entries[0],
            typeof(NodeBrowseObjectProvider),
            LogLevel.Information,
            new EventId(1, "BrowseObjectRequested"),
            "Getting browse object for {NodeMoniker}.");
        entries[0].Properties["NodeMoniker"].Should().Be(
            @"C:\workspace\src\main.rs");
        entries[0].Exception.Should().BeNull();
        var failure = entries.Single(entry =>
            entry.Category == typeof(NodeBrowseObjectProvider).FullName
            && entry.EventId == new EventId(
                2,
                "BrowseObjectUpdateFailed"));
        AssertEntry(
            failure,
            typeof(NodeBrowseObjectProvider),
            LogLevel.Error,
            new EventId(2, "BrowseObjectUpdateFailed"),
            "Operation '{Operation}' failed unexpectedly.");
        failure.Properties["Operation"].Should().Be(
            "NodeBrowseObjectProvider.BrowseObject_PropertyChanged");
        failure.Exception.Should().BeOfType<NullReferenceException>();
        settings.Verify(
            value => value.SetAsync(
                SettingsInfo.TypeDebuggerEnvironment,
                It.IsAny<PathEx>(),
                "cancelled"),
            Times.Once);
        provider.Entries.Count(entry =>
                entry.Category == typeof(NodeBrowseObjectProvider).FullName
                && entry.EventId == new EventId(
                    2,
                    "BrowseObjectUpdateFailed"))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task NodeLegacyConstructorDeliversOneFaultAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        LegacyLogger legacy = new LegacyLoggerBridge(
            factory.CreateLogger("Legacy.Node"));
        var nodeProvider = new NodeBrowseObjectProvider(
            legacy,
            readiness.Policy);
        var observe = typeof(NodeBrowseObjectProvider).GetMethod(
            "ObserveUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        observe.Should().NotBeNull();
        observe.Invoke(
            nodeProvider,
            new object[]
            {
                Task.FromException(
                    new InvalidOperationException("legacy failure")),
            });

        provider.Entries.Should().ContainSingle();
        provider.Entries.Single().Category.Should().Be("Legacy.Node");
    }

    [Fact]
    public async Task DebuggerLogsStructuredLaunchBranchesAndPreservesNotificationsAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var notifications =
            new ConcurrentQueue<(string Message, string Diagnostic)>();
        var fileExists = false;
        var binPathQueries = 0;
        var launches = 0;
        var debugProvider = CreateDebugProvider(
            _ => fileExists,
            (message, diagnostic) =>
            {
                notifications.Enqueue((message, diagnostic));
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                binPathQueries++;
                return Task.FromResult(((PathEx)@"C:\bin", (PathEx)@"C:\lib"));
            },
            (_, _) => launches++);
        debugProvider.LoggerFactory = factory;
        debugProvider.L = Mock.Of<LegacyLogger>();
        var telemetry = new RecordingFeatureUsageTelemetry();
        debugProvider.UsageTelemetry = telemetry;
        debugProvider.AvailabilityPolicy = readiness.Policy;

        var model = new WorkspaceModel
        {
            TargetDirectory = (PathEx)@"C:\workspace\target",
            WorkspaceRoot = (PathEx)@"C:\workspace",
        };
        var package = new WorkspaceModel.Package
        {
            ManifestPath = (PathEx)@"C:\workspace\Cargo.toml",
            Name = "sample",
        };
        model.Packages.Add(package);
        var target = new WorkspaceModel.Target
        {
            CrateTypes = new[] { WorkspaceModel.CrateType.Bin, },
            Kinds = new[] { WorkspaceModel.Kind.Bin, },
            Name = "sample",
            SourcePath = (PathEx)@"C:\workspace\src\main.rs",
        };
        var metadata = new Mock<IMetadataService>();
        metadata.Setup(
                value => value.GetContainingPackageAsync(
                    It.IsAny<PathEx>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => package);
        var configuration = new Mock<IPropertySettings>();
        var targetName = "missing target";
        configuration.Setup(
                value => value.ContainsKey(
                    LaunchConfigurationConstants.ProgramKey))
            .Returns(true);
        configuration.Setup(
                value => value.ContainsKey(
                    LaunchConfigurationConstants.NameKey))
            .Returns(true);
        configuration.Setup(
                value => value.ContainsKey(
                    LaunchConfigurationConstants.ProjectKey))
            .Returns(true);
        configuration.SetupGet(value => value.Count).Returns(3);
        configuration.Setup(
                value => value[
                    LaunchConfigurationConstants.ProgramKey])
            .Returns("program");
        configuration.Setup(
                value => value[
                    LaunchConfigurationConstants.NameKey])
            .Returns(() => targetName);
        configuration.Setup(
                value => value[
                    LaunchConfigurationConstants.ProjectKey])
            .Returns("project");
        var settings = new Mock<ISettingsService>();
        settings.Setup(
                value => value.GetAsync(
                    It.IsAny<string>(),
                    It.IsAny<PathEx>()))
            .ReturnsAsync(string.Empty);
        var projectConfiguration =
            new Mock<IProjectConfigurationService>();
        projectConfiguration.Setup(
                value => value.GetActiveProjectBuildConfiguration(
                    It.IsAny<ProjectTargetFileContext>()))
            .Returns("dev");
        var workspace = new Mock<IWorkspace>();
        workspace.Setup(value => value.GetService(typeof(IMetadataService)))
            .Returns(metadata.Object);
        workspace.Setup(
                value => value.GetService(
                    typeof(IProjectConfigurationService)))
            .Returns(projectConfiguration.Object);
        workspace.Setup(value => value.GetService(typeof(ISettingsService)))
            .Returns(settings.Object);
        workspace.SetupGet(value => value.JTF)
            .Returns(readiness.Context.Factory);
        var launchContext = new DebugLaunchActionContext(
            configuration.Object,
            debugProvider,
            null,
            null);

        debugProvider.LaunchDebugTarget(
            workspace.Object,
            null,
            launchContext);

        package.Targets.Add(target);
        targetName = target.QualifiedTargetFileName;
        debugProvider.LaunchDebugTarget(
            workspace.Object,
            null,
            launchContext);

        fileExists = true;
        debugProvider.LaunchDebugTarget(
            workspace.Object,
            null,
            launchContext);

        var expected = new InvalidOperationException("metadata failed");
        metadata.Setup(
                value => value.GetContainingPackageAsync(
                    It.IsAny<PathEx>(),
                    It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        ((Action)(() => debugProvider.LaunchDebugTarget(
                workspace.Object,
                null,
                launchContext)))
            .Should()
            .Throw<InvalidOperationException>()
            .Which.Should().BeSameAs(expected);

        configuration.Setup(
                value => value.ContainsKey(
                    LaunchConfigurationConstants.ProgramKey))
            .Returns(false);
        debugProvider.LaunchDebugTarget(
            workspace.Object,
            null,
            launchContext);

        var entries = provider.Entries.ToArray();
        entries.Should().HaveCount(5);
        var missingTarget = GetEntry(
            entries,
            typeof(DebugLaunchTargetProvider),
            new EventId(1, "LaunchTargetNotFound"));
        missingTarget.Level.Should().Be(LogLevel.Error);
        missingTarget.Template.Should().Be(
            "Cannot find target '{TargetName}' in '{PackagePath}', for profile '{Profile}'.");
        missingTarget.Properties["TargetName"].Should().Be(
            "missing target");
        missingTarget.Properties["PackagePath"].Should().Be(
            package.FullPath);
        missingTarget.Properties["Profile"].Should().Be("dev");
        missingTarget.Exception.Should().BeNull();
        var missingExecutable = GetEntry(
            entries,
            typeof(DebugLaunchTargetProvider),
            new EventId(2, "LaunchExecutableNotFound"));
        missingExecutable.Level.Should().Be(LogLevel.Information);
        missingExecutable.Template.Should().Be(
            "Unable to find file: '{ExecutablePath}'.");
        missingExecutable.Properties["ExecutablePath"].Should().Be(
            target.GetPath("dev"));
        missingExecutable.Exception.Should().BeNull();
        var prepared = GetEntry(
            entries,
            typeof(DebugLaunchTargetProvider),
            new EventId(3, "LaunchTargetPrepared"));
        prepared.Level.Should().Be(LogLevel.Information);
        prepared.Template.Should().Be(
            "LaunchDebugTarget with profile: {Profile}, launchConfiguration: {LaunchConfiguration}");
        prepared.Properties["Profile"].Should().Be("dev");
        prepared.Properties["LaunchConfiguration"].Should().NotBeNull();
        prepared.Exception.Should().BeNull();
        var failed = GetEntry(
            entries,
            typeof(DebugLaunchTargetProvider),
            new EventId(4, "LaunchFailed"));
        failed.Level.Should().Be(LogLevel.Error);
        failed.Template.Should().Be(
            "Operation '{Operation}' failed unexpectedly.");
        failed.Properties["Operation"].Should().Be(
            "DebugLaunchTargetProvider.LaunchDebugTargetAsync");
        failed.Exception.Should().BeSameAs(expected);
        var invalidKey = GetEntry(
            entries,
            typeof(DebugLaunchTargetProvider.LaunchConfigWrapper),
            new EventId(1, "LaunchConfigurationKeyInvalid"));
        invalidKey.Level.Should().Be(LogLevel.Error);
        invalidKey.Template.Should().Be(
            "Key '{Key}' is not set in launch configuration and / or is not a string.");
        invalidKey.Properties["Key"].Should().Be(
            LaunchConfigurationConstants.ProgramKey);
        invalidKey.Exception.Should().BeOfType<KeyNotFoundException>();

        notifications.Select(value => value.Message).Should().Equal(
            "Cannot find target 'missing target' in 'C:\\workspace\\Cargo.toml', for profile 'dev'.",
            $"Unable to find file: '{target.GetPath("dev")}'.",
            "Key 'program' is not set in launch configuration and / or is not a string.");
        notifications.Take(2).Should().OnlyContain(value =>
            value.Diagnostic ==
                "Delete the .vs folder and try again. If that does not work please file a bug with the repro steps.");
        notifications.Last().Diagnostic.Should().Be(
            "Debugger will not be launched. Please report the repro steps + this message as this issue is hard to track down. 🙏");
        binPathQueries.Should().Be(1);
        launches.Should().Be(1);
        telemetry.Events.Select(value => value.Outcome).Should().Equal(
            UsageOutcome.Failed,
            UsageOutcome.Failed,
            UsageOutcome.Succeeded,
            UsageOutcome.Failed,
            UsageOutcome.Failed);
    }

    [Fact]
    public async Task CommandObserversUseOwnerCategoriesWithoutDuplicatesAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        var propagationFailure =
            new InvalidOperationException("propagation failed");
        using var failureProvider = new CategoryFaultingLoggerProvider(
            typeof(ToolchainServiceExtensions).FullName,
            propagationFailure);
        using var factory = new LoggerFactory(
            new ILoggerProvider[] { provider, failureProvider, });
        var installFailure =
            new InvalidOperationException("install failed");
        var installLogger = factory.CreateLogger(
            typeof(InstallToolchainCommand).FullName);
        ObserveBackgroundOperation(
            typeof(InstallToolchainCommand),
            installLogger,
            "InstallToolchainCommand.ExecuteCoreAsync",
            Task.FromException(installFailure));
        ObserveBackgroundOperation(
            typeof(InstallToolchainCommand),
            installLogger,
            "InstallToolchainCommand.ExecuteCoreAsync",
            Task.FromCanceled(new CancellationToken(true)));
        var toolchainSwitch = CreateSwitchCommand(
            readiness.State,
            readiness.Policy);
        SetBaseField(toolchainSwitch, "_loggerFactory", factory);
        SetBaseField(
            toolchainSwitch,
            "_usageTelemetry",
            new RecordingFeatureUsageTelemetry());
        var solution = new Mock<IVsSolution>();
        string workspaceRoot = @"C:\workspace";
        string solutionFile = null;
        string userOptionsFile = null;
        solution.Setup(value => value.GetSolutionInfo(
                out workspaceRoot,
                out solutionFile,
                out userOptionsFile))
            .Returns(Microsoft.VisualStudio.VSConstants.S_OK);
        SetBaseField(toolchainSwitch, "_solution", solution.Object);
        toolchainSwitch.Command.Properties["name"] = "stable";
        var jtf = typeof(RustAnalyzerPackage).GetField(
            "_jtf",
            BindingFlags.Static | BindingFlags.NonPublic);
        jtf.Should().NotBeNull();
        var originalJtf = jtf.GetValue(null);
        try
        {
            jtf.SetValue(null, readiness.Context.Factory);
            InvokeCommand(toolchainSwitch, toolchainSwitch.Command);
            var switchLogger = GetCommandLogger(toolchainSwitch);
            ObserveBackgroundOperation(
                typeof(SwitchToolchainCommand),
                switchLogger,
                "SwitchToolchainCommand.ExecuteCore",
                Task.FromCanceled(new CancellationToken(true)));

            ((Action)(() => InvokeCommand(
                    toolchainSwitch,
                    new object())))
                .Should()
                .Throw<TargetInvocationException>()
                .Which.InnerException.Should().BeOfType<InvalidCastException>();
        }
        finally
        {
            jtf.SetValue(null, originalJtf);
        }

        var entries = provider.Entries.ToArray();
        entries.Should().HaveCount(4);
        var installObserverFailure = GetEntry(
            entries,
            typeof(InstallToolchainCommand),
            new EventId(2, "BackgroundOperationFailed"));
        installObserverFailure.Level.Should().Be(LogLevel.Error);
        installObserverFailure.Template.Should().Be(
            "Operation '{Operation}' failed unexpectedly.");
        installObserverFailure.Properties["Operation"].Should().Be(
            "InstallToolchainCommand.ExecuteCoreAsync");
        installObserverFailure.Exception.Should().BeSameAs(installFailure);
        var switchCommandFailure = GetEntry(
            entries,
            typeof(SwitchToolchainCommand),
            new EventId(1, "CommandExecutionFailed"));
        switchCommandFailure.Level.Should().Be(LogLevel.Error);
        switchCommandFailure.Template.Should().Be(
            "Operation '{Operation}' failed unexpectedly.");
        switchCommandFailure.Properties["Operation"].Should().Be(
            "BaseRustAnalyzerCommand.Execute");
        switchCommandFailure.Exception.Should()
            .BeOfType<InvalidCastException>();
        var switchFailure = GetEntry(
            entries,
            typeof(SwitchToolchainCommand),
            new EventId(2, "BackgroundOperationFailed"));
        switchFailure.Template.Should().Be(
            "Operation '{Operation}' failed unexpectedly.");
        switchFailure.Level.Should().Be(LogLevel.Error);
        switchFailure.Properties["Operation"].Should().Be(
            "SwitchToolchainCommand.ExecuteCore");
        switchFailure.Exception.Should().BeSameAs(propagationFailure);
        var propagated = GetEntry(
            entries,
            typeof(ToolchainServiceExtensions),
            new EventId(1, "ToolchainOverrideCommandStarted"));
        propagated.Properties["ExecutableName"].Should().Be("rustup");
        propagated.Properties["Arguments"].Should().Be(
            "override set stable");
        entries.Where(entry =>
                entry.Category == typeof(SwitchToolchainCommand).FullName)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(entry =>
                entry.EventId == new EventId(
                    1,
                    "CommandExecutionFailed")
                || entry.EventId == new EventId(
                    2,
                    "BackgroundOperationFailed"));
        entries.Where(entry =>
                entry.Category == typeof(SwitchToolchainCommand).FullName)
            .Select(entry => entry.EventId.Id)
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RetainedLegacyBoundariesDeliverWithoutDuplicatesAsync()
    {
        using var readiness = await ReadyFixture.CreateAsync();
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var registry = new Mock<IRegistrySettingsService>();
        registry.SetupGet(value => value.InfoBarDismissedByUser)
            .Returns(true);
        var jtf = typeof(RustAnalyzerPackage).GetField(
            "_jtf",
            BindingFlags.Static | BindingFlags.NonPublic);
        jtf.Should().NotBeNull();
        var originalJtf = jtf.GetValue(null);
        try
        {
            jtf.SetValue(null, readiness.Context.Factory);
            await RustAnalyzerPackage.ReleaseSummaryNotification.ShowAsync(
                registry.Object,
                new TL
                {
                    L = new LegacyLoggerBridge(
                        factory.CreateLogger("Legacy.ReleaseSummary")),
                });
        }
        finally
        {
            jtf.SetValue(null, originalJtf);
        }

        var launchSettings = new Mock<IPropertySettings>();
        launchSettings.Setup(value => value.ContainsKey("missing"))
            .Returns(false);
        var launchConfiguration =
            new DebugLaunchTargetProvider.LaunchConfigWrapper(
                launchSettings.Object,
                new LegacyLoggerBridge(
                    factory.CreateLogger("Legacy.LaunchConfig")));
        ((Func<string>)(() => launchConfiguration["missing"]))
            .Should()
            .Throw<KeyNotFoundException>();

        var contextFactory = new FileContextProviderFactory
        {
            AvailabilityPolicy = readiness.Policy,
            L = new LegacyLoggerBridge(
                factory.CreateLogger("Legacy.FileContextFactory")),
            LazyCargoService = new Lazy<IToolchainService>(
                () => Mock.Of<IToolchainService>()),
            OutputPane = Mock.Of<IBuildOutputSink>(),
        };
        contextFactory.CreateProvider(Mock.Of<IWorkspace>());
        var scannerFactory = new FileScannerFactory
        {
            AvailabilityPolicy = readiness.Policy,
            L = new LegacyLoggerBridge(
                factory.CreateLogger("Legacy.FileScannerFactory")),
        };
        scannerFactory.CreateProvider(Mock.Of<IWorkspace>());

        var commandServices = new CmdServices(() => null);
        SetField(
            commandServices,
            typeof(CmdServices),
            "_mef",
            Mock.Of<IComponentModel2>());
        SetField(
            commandServices,
            typeof(CmdServices),
            "_l",
            new LegacyLoggerBridge(
                factory.CreateLogger("Legacy.CmdServices")));
        var solution = new Mock<IVsSolution>();
        string solutionDirectory = null;
        string solutionFile = null;
        string userOptionsFile = null;
        solution.Setup(value => value.GetSolutionInfo(
                out solutionDirectory,
                out solutionFile,
                out userOptionsFile))
            .Returns(Microsoft.VisualStudio.VSConstants.E_FAIL);
        SetField(
            commandServices,
            typeof(CmdServices),
            "_solution",
            solution.Object);
        var debugger = new Mock<IVsDebugger>();
        debugger.Setup(value => value.GetMode(
                It.IsAny<DBGMODE[]>()))
            .Returns(Microsoft.VisualStudio.VSConstants.E_FAIL);
        SetField(
            commandServices,
            typeof(CmdServices),
            "_debugger",
            debugger.Object);
        var threadContext = typeof(ThreadHelper).GetField(
            "_joinableTaskContextCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        threadContext.Should().NotBeNull();
        var originalThreadContext = threadContext.GetValue(null);
        var genericThreadHelper = typeof(ThreadHelper).GetField(
            "_generic",
            BindingFlags.Static | BindingFlags.NonPublic);
        genericThreadHelper.Should().NotBeNull();
        var originalGenericThreadHelper =
            genericThreadHelper.GetValue(null);
        try
        {
            threadContext.SetValue(null, readiness.Context);
            genericThreadHelper.SetValue(null, null);
            typeof(ThreadHelper).GetMethod(
                    "SetUIThread",
                    BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, Array.Empty<object>());
            ((Func<PathEx>)(() => commandServices.GetWorkspaceRoot()))
                .Should()
                .Throw<InvalidOperationException>();
            commandServices.IsIdeInDesignMode().Should().BeFalse();
        }
        finally
        {
            genericThreadHelper.SetValue(
                null,
                originalGenericThreadHelper);
            threadContext.SetValue(null, originalThreadContext);
        }

        LegacyLogger commandLogger = new LegacyLoggerBridge(
            factory.CreateLogger("Legacy.Command"));
        var commandFailure = new InvalidOperationException("command failure");
        var command = new FaultingCommand(
            readiness.State,
            commandFailure);
        var nullFactory = new Mock<ILoggerFactory>();
        nullFactory.Setup(value => value.CreateLogger(It.IsAny<string>()))
            .Returns((Microsoft.Extensions.Logging.ILogger)null);
        SetBaseField(command, "_loggerFactory", nullFactory.Object);
        SetBaseField(command, "_logger", commandLogger);

        ((Action)command.Invoke)
            .Should()
            .Throw<InvalidOperationException>()
            .Which.Should().BeSameAs(commandFailure);

        provider.Entries.Where(entry =>
                entry.Category == "Legacy.ReleaseSummary")
            .Select(entry => entry.Message)
            .Should()
            .Equal(
                "Attempting to show release notes...",
                "... Not showing release notes as it has already been dismissed by the user.");
        provider.Entries.Where(entry =>
                entry.Category == "Legacy.LaunchConfig")
            .Should()
            .ContainSingle();
        provider.Entries.Where(entry =>
                entry.Category == "Legacy.FileContextFactory")
            .Should()
            .ContainSingle();
        provider.Entries.Where(entry =>
                entry.Category == "Legacy.FileScannerFactory")
            .Should()
            .ContainSingle();
        provider.Entries.Where(entry =>
                entry.Category == "Legacy.Command")
            .Should()
            .ContainSingle();
        provider.Entries.Where(entry =>
                entry.Category == "Legacy.CmdServices")
            .Select(entry => entry.Message)
            .Should()
            .Equal(
                "Unable to determine workspace root.",
                "Unable to determine debugger mode.");
        provider.Entries
            .GroupBy(entry => entry.Category)
            .Should()
            .OnlyContain(group => group.Count() ==
                (group.Key == "Legacy.ReleaseSummary"
                    || group.Key == "Legacy.CmdServices"
                        ? 2
                        : 1));
    }

    private static RustAnalyzerPackage CreatePackage(
        JoinableTaskContext context)
    {
        var contextField = typeof(ThreadHelper).GetField(
            "_joinableTaskContextCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        contextField.Should().NotBeNull();
        lock (contextField)
        {
            var original = contextField.GetValue(null);
            try
            {
                contextField.SetValue(null, context);
                return new RustAnalyzerPackage();
            }
            finally
            {
                contextField.SetValue(null, original);
            }
        }
    }

    private static void AddPackageService(
        AsyncPackage package,
        Type serviceType,
        object service)
    {
        var servicesField = typeof(AsyncPackage).GetField(
            "asyncServices",
            BindingFlags.Instance | BindingFlags.NonPublic);
        servicesField.Should().NotBeNull();
        var services = servicesField.GetValue(package)
            as IDictionary<Guid, object>;
        services.Should().NotBeNull();
        services[serviceType.GUID] = new AsyncLazy<object>(
            () => Task.FromResult(service),
            package.JoinableTaskFactory);
    }

    private static DebugLaunchTargetProvider CreateDebugProvider(
        Func<string, bool> fileExists,
        Func<string, string, Task> showMessageBoxAsync,
        Func<PathEx, CancellationToken, Task<(PathEx Bin, PathEx Lib)>>
            getBinAndLibPathsAsync,
        Action<IServiceProvider, VsDebugTargetInfo> launchDebugger)
    {
        var constructor = typeof(DebugLaunchTargetProvider)
            .GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        return (DebugLaunchTargetProvider)constructor.Invoke(
            new object[]
            {
                fileExists,
                showMessageBoxAsync,
                getBinAndLibPathsAsync,
                launchDebugger,
            });
    }

    private static SwitchToolchainCommand CreateSwitchCommand(
        PrerequisiteProcessState state,
        PrerequisiteAvailabilityPolicy policy)
    {
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
        return (SwitchToolchainCommand)constructor.Invoke(
            new object[] { state, policy, (Action)(() => { }), });
    }

    private static Microsoft.Extensions.Logging.ILogger GetCommandLogger<T>(
        BaseRustAnalyzerCommand<T> command)
        where T : class, new()
    {
        var property = typeof(BaseRustAnalyzerCommand<T>).GetProperty(
            "MelLogger",
            BindingFlags.Instance | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        return (Microsoft.Extensions.Logging.ILogger)property.GetValue(
            command);
    }

    private static void InvokeCommand<T>(
        BaseRustAnalyzerCommand<T> command,
        object sender)
        where T : class, new()
    {
        var execute = typeof(BaseRustAnalyzerCommand<T>).GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic);
        execute.Should().NotBeNull();
        execute.Invoke(
            command,
            new object[] { sender, EventArgs.Empty, });
    }

    private static void ObserveBackgroundOperation(
        Type commandType,
        Microsoft.Extensions.Logging.ILogger logger,
        string operation,
        Task task)
    {
        var observe = commandType.GetMethod(
            "ObserveBackgroundOperation",
            BindingFlags.Static | BindingFlags.NonPublic);
        observe.Should().NotBeNull();
        observe.Invoke(
            null,
            new object[] { task, logger, operation, });
    }

    private static RecordingLogEntry GetEntry(
        RecordingLogEntry[] entries,
        Type category,
        EventId eventId)
    {
        return entries.Single(entry =>
            entry.Category == category.FullName
            && entry.EventId == eventId);
    }

    private static void AssertEntry(
        RecordingLogEntry entry,
        Type category,
        LogLevel level,
        EventId eventId,
        string template)
    {
        entry.Category.Should().Be(category.FullName);
        entry.Level.Should().Be(level);
        entry.EventId.Should().Be(eventId);
        entry.EventId.Id.Should().BePositive();
        entry.Template.Should().Be(template);
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

    private static void SetField(
        object instance,
        Type declaringType,
        string name,
        object value)
    {
        var field = declaringType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field.SetValue(instance, value);
    }

    private static void SetProperty(
        object instance,
        Type declaringType,
        string name,
        object value)
    {
        const BindingFlags Flags = BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic;
        var property = declaringType.GetProperty(
            name,
            Flags);
        property.Should().NotBeNull();
        property.SetValue(instance, value);
    }

    private sealed class CategoryFaultingLoggerProvider : ILoggerProvider
    {
        private readonly string _category;
        private readonly Exception _exception;

        public CategoryFaultingLoggerProvider(
            string category,
            Exception exception)
        {
            _category = category;
            _exception = exception;
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(
            string categoryName)
        {
            return new CategoryFaultingLogger(
                categoryName,
                _category,
                _exception);
        }

        public void Dispose()
        {
        }

        private sealed class CategoryFaultingLogger
            : Microsoft.Extensions.Logging.ILogger
        {
            private readonly string _category;
            private readonly string _faultingCategory;
            private readonly Exception _exception;

            public CategoryFaultingLogger(
                string category,
                string faultingCategory,
                Exception exception)
            {
                _category = category;
                _faultingCategory = faultingCategory;
                _exception = exception;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (_category == _faultingCategory)
                {
                    throw _exception;
                }
            }
        }
    }

    private sealed class FaultingCommand
        : BaseRustAnalyzerCommand<FaultingCommand>
    {
        private readonly Exception _exception;

        public FaultingCommand()
            : this(PrerequisiteProcessState.Current, new InvalidOperationException())
        {
        }

        public FaultingCommand(
            PrerequisiteProcessState state,
            Exception exception)
            : base(state)
        {
            _exception = exception;
        }

        public void Invoke()
        {
            Execute(this, EventArgs.Empty);
        }

        protected override void ExecuteCore(
            object sender,
            OleMenuCmdEventArgs eventArgs)
        {
            throw _exception;
        }
    }

    private sealed class ReadyFixture : IDisposable
    {
        private ReadyFixture(
            JoinableTaskContext context,
            PrerequisiteProcessState state,
            PrerequisiteAvailabilityPolicy policy)
        {
            Context = context;
            State = state;
            Policy = policy;
        }

        public JoinableTaskContext Context { get; }

        public PrerequisiteAvailabilityPolicy Policy { get; }

        public PrerequisiteProcessState State { get; }

        public static async Task<ReadyFixture> CreateAsync()
        {
            var context = new JoinableTaskContext();
            var state = new PrerequisiteProcessState(context.Factory);
            await state.GetOrEvaluateAsync(
                _ => Task.FromResult(PrerequisiteResult.Success),
                default);
            return new ReadyFixture(
                context,
                state,
                new PrerequisiteAvailabilityPolicy(
                    state,
                    Mock.Of<LegacyLogger>()));
        }

        public void Dispose()
        {
            Context.Dispose();
        }
    }
}
