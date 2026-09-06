using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.Shell;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Moq;
using Xunit;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class OutputWindowLoggerProviderTests
{
    [Fact]
    public async Task CoalescesFifoWritesAndPreservesStructuredSemanticsAsync()
    {
        var writes = new ConcurrentQueue<string>();
        var scheduledDrains = new ConcurrentQueue<Func<Task>>();
        var onUiBoundary = false;
        var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
        pane.Setup(value => value.OutputStringThreadSafe(It.IsAny<string>()))
            .Callback(
                (string value) =>
                {
                    onUiBoundary.Should().BeTrue();
                    writes.Enqueue(value);
                })
            .Returns(VSConstants.S_OK);
        var paneAcquisitions = 0;
        var faults = new ConcurrentQueue<Exception>();
        using var provider = CreateProvider(
            action =>
            {
                scheduledDrains.Enqueue(
                    async () =>
                    {
                        onUiBoundary = true;
                        try
                        {
                            await action();
                        }
                        finally
                        {
                            onUiBoundary = false;
                        }
                    });
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                onUiBoundary.Should().BeTrue();
                Interlocked.Increment(ref paneAcquisitions);
                return pane.Object;
            },
            faults.Enqueue);
        using var factory = CreateFactory(provider);
        var discoveryLogger = factory.CreateLogger("Adapter.Discovery");
        var executionLogger = factory.CreateLogger("Adapter.Execution");
        var exception = new InvalidOperationException("failure detail");

        discoveryLogger.LogDebug("filtered");
        using (discoveryLogger.BeginScope("Invocation {Id}", 42))
        {
            discoveryLogger.LogInformation(
                new EventId(7, "Started"),
                "Processed {Crate}",
                "workspace");
            executionLogger.LogError(
                new EventId(9, "Failed"),
                exception,
                "Failed {Crate}",
                "workspace");
        }

        scheduledDrains.Should().ContainSingle();
        paneAcquisitions.Should().Be(0);
        writes.Should().BeEmpty();

        scheduledDrains.TryDequeue(out var firstDrain).Should().BeTrue();
        await firstDrain();

        paneAcquisitions.Should().Be(1);
        writes.Should().HaveCount(2);
        writes.ElementAt(0).Should().Contain("[Information] Adapter.Discovery");
        writes.ElementAt(0).Should().Contain("EventId=7(Started)");
        writes.ElementAt(0).Should().Contain("Processed workspace");
        writes.ElementAt(0).Should().Contain("Template: Processed {Crate}");
        writes.ElementAt(0).Should().Contain("Properties: Crate=workspace");
        writes.ElementAt(0).Should().Contain("Invocation 42");
        writes.ElementAt(1).Should().Contain("[Error] Adapter.Execution");
        writes.ElementAt(1).Should().Contain(exception.ToString());
        faults.Should().BeEmpty();

        discoveryLogger.LogWarning("later");
        scheduledDrains.Should().ContainSingle();
        scheduledDrains.TryDequeue(out var secondDrain).Should().BeTrue();
        await secondDrain();

        paneAcquisitions.Should().Be(1);
        pane.Verify(
            value => value.OutputStringThreadSafe(It.IsAny<string>()),
            Times.Exactly(3));
        pane.Verify(value => value.Activate(), Times.Never);
    }

    [Fact]
    public async Task ProviderFailuresDoNotEscapeOrRecurseAsync()
    {
        var observed = new ConcurrentQueue<Exception>();
        Func<Task> drain = null;
        var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
        pane.Setup(value => value.OutputStringThreadSafe(It.IsAny<string>()))
            .Returns(VSConstants.E_FAIL);
        using var provider = CreateProvider(
            action =>
            {
                drain = action;
                return Task.CompletedTask;
            },
            (_, _) => pane.Object,
            observed.Enqueue);
        using var factory = CreateFactory(provider);
        var logger = factory.CreateLogger("Product.Operation");
        var productException = new InvalidOperationException("product failure");

        var loggingFailure = Record.Exception(
            () => logger.LogError(productException, "message"));
        loggingFailure.Should().BeNull();
        await drain();

        observed.Should().ContainSingle();
        observed.Single().Should().NotBeSameAs(productException);

        var schedulingFailure = new InvalidOperationException("JTF failure");
        using var schedulingProvider = CreateProvider(
            _ => throw schedulingFailure,
            (_, _) => pane.Object,
            observed.Enqueue);
        using var schedulingFactory = CreateFactory(schedulingProvider);
        Record.Exception(
                () => schedulingFactory
                    .CreateLogger("Product.Scheduling")
                    .LogError(productException, "message"))
            .Should()
            .BeNull();
        observed.Should().Contain(schedulingFailure);
    }

    [Fact]
    public void DrainsOnTheJtfOwnerThread()
    {
        var synchronizationContext =
            new SingleThreadedSynchronizationContext();
        using var context = new JoinableTaskContext(
            Thread.CurrentThread,
            synchronizationContext);
        RunOnOwnerThread(
            context,
            synchronizationContext,
            async () =>
            {
                var ownerThread = Thread.CurrentThread.ManagedThreadId;
                var paneThread = 0;
                var writeThread = 0;
                var written = NewSignal();
                var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
                pane.Setup(
                        value => value.OutputStringThreadSafe(
                            It.IsAny<string>()))
                    .Callback(
                        () =>
                        {
                            writeThread = Thread.CurrentThread.ManagedThreadId;
                            written.TrySetResult(null);
                        })
                    .Returns(VSConstants.S_OK);
                using var provider = CreateProvider(
                    action => context.Factory.RunAsync(
                        async () =>
                        {
                            await context.Factory.SwitchToMainThreadAsync();
                            await action();
                        }).Task,
                    (_, _) =>
                    {
                        paneThread = Thread.CurrentThread.ManagedThreadId;
                        return pane.Object;
                    },
                    _ => { });
                using var factory = CreateFactory(provider);
                var logger = factory.CreateLogger("Thread.Affinity");
                var callerThread = 0;
                var thread = new Thread(
                    () =>
                    {
                        callerThread = Thread.CurrentThread.ManagedThreadId;
                        logger.LogInformation("message");
                    });

                thread.Start();
                thread.Join();
                await written.Task;

                callerThread.Should().NotBe(ownerThread);
                paneThread.Should().Be(ownerThread);
                writeThread.Should().Be(ownerThread);
            });
    }

    [Fact]
    public void HasOneSharedStandardFactoryAndNoTelemetryProvider()
    {
        var provider = CreateProvider(
            _ => Task.CompletedTask,
            (_, _) => Mock.Of<IVsOutputWindowPane>(),
            _ => { });
        using var factory = new VsixLoggerFactory(provider);

        factory.Should().BeAssignableTo<LoggerFactory>();
        typeof(VsixLoggerFactory)
            .GetCustomAttributes<PartCreationPolicyAttribute>()
            .Single()
            .CreationPolicy
            .Should()
            .Be(CreationPolicy.Shared);
        typeof(VsixLoggerFactory)
            .GetCustomAttributes<ExportAttribute>()
            .Should()
            .ContainSingle(attribute =>
                attribute.ContractType == typeof(ILoggerFactory));

        var productAssemblies = new[]
        {
            typeof(VsixLoggerFactory).Assembly,
            typeof(VSTestLoggerProvider).Assembly,
        };
        productAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                typeof(ILoggerProvider).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    typeof(OutputWindowLoggerProvider),
                    typeof(VSTestLoggerProvider),
                });
        productAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(LegacyLogger).IsAssignableFrom(type))
            .Where(type => type
                .GetCustomAttributes<ExportAttribute>()
                .Any(attribute =>
                    attribute.ContractType == typeof(LegacyLogger)))
            .Should()
            .Equal(typeof(LegacyLoggerBridge));

        var loggingTypes = new[]
        {
            typeof(OutputWindowLoggerProvider),
            typeof(VsixLoggerFactory),
            typeof(VSTestLoggerProvider),
            typeof(TestAdapterLogger),
            typeof(LegacyLoggerBridge),
        };
        var dependencyTypes = loggingTypes
            .SelectMany(type => type
                .GetConstructors(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType))
            .Concat(loggingTypes.SelectMany(type => type
                .GetFields(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly)
                .Select(field => field.FieldType)));
        dependencyTypes.Should().NotContain(type =>
            typeof(IFeatureUsageTelemetry).IsAssignableFrom(type)
            || (type.Namespace != null
                && type.Namespace.StartsWith(
                    "Microsoft.ApplicationInsights",
                    StringComparison.Ordinal)));
    }

    private static LoggerFactory CreateFactory(ILoggerProvider provider)
    {
        return new LoggerFactory(
            new[] { provider, },
            new LoggerFilterOptions { MinLevel = LogLevel.Information, });
    }

    private static OutputWindowLoggerProvider CreateProvider(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane,
        Action<Exception> observeFault)
    {
        var constructor = typeof(OutputWindowLoggerProvider).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(Func<Func<Task>, Task>),
                typeof(Func<Guid, string, IVsOutputWindowPane>),
                typeof(Action<Exception>),
            },
            null);
        constructor.Should().NotBeNull();
        return (OutputWindowLoggerProvider)constructor.Invoke(
            new object[] { runOnMainThreadAsync, getOrCreatePane, observeFault, });
    }

    private static void RunOnOwnerThread(
        JoinableTaskContext context,
        SingleThreadedSynchronizationContext synchronizationContext,
        Func<Task> action)
    {
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(
                synchronizationContext);
            var operation = context.Factory.RunAsync(action);
            var frame = new SingleThreadedSynchronizationContext.Frame();
            _ = operation.Task.ContinueWith(
                _ => synchronizationContext.Post(
                    _ => frame.Continue = false,
                    null),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            synchronizationContext.PushFrame(frame);
            operation.Join();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static TaskCompletionSource<object> NewSignal()
    {
        return new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

[Trait("type", "UnitTests")]
public sealed class BuildOutputSinkTests
{
    [Fact]
    public async Task RoutesStringAndDetailedMessagesThroughTheBuildPaneAsync()
    {
        var writes = new ConcurrentQueue<string>();
        var stringWritten = NewSignal();
        var detailReported = NewSignal();
        var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
        pane.Setup(value => value.Activate()).Returns(VSConstants.S_OK);
        pane.Setup(value => value.OutputStringThreadSafe(It.IsAny<string>()))
            .Callback(
                (string value) =>
                {
                    writes.Enqueue(value);
                    stringWritten.TrySetResult(null);
                })
            .Returns(VSConstants.S_OK);
        var acquisitions = new ConcurrentQueue<(Guid Id, string Name)>();
        var faults = new ConcurrentQueue<Exception>();
        var sink = Create(
            action => Task.Run(action),
            (id, name) =>
            {
                acquisitions.Enqueue((id, name));
                return pane.Object;
            },
            faults.Enqueue);
        var detail = new DetailedBuildMessage();
        BuildMessage reportedDetail = null;

        sink.WriteLine(
            (PathEx)@"C:\workspace",
            _ => Task.CompletedTask,
            new StringBuildMessage { Message = "build text", });
        await stringWritten.Task;
        sink.WriteLine(
            (PathEx)@"C:\workspace",
            message =>
            {
                reportedDetail = message;
                detailReported.TrySetResult(null);
                return Task.CompletedTask;
            },
            detail);
        await detailReported.Task;

        acquisitions.Should().ContainSingle();
        acquisitions.Single().Id.Should().Be(
            VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid);
        writes.Should().ContainSingle();
        writes.Single().Should().Contain("build text");
        reportedDetail.Should().BeSameAs(detail);
        faults.Should().BeEmpty();
        pane.Verify(value => value.Activate(), Times.Exactly(2));
        pane.Verify(
            value => value.OutputStringThreadSafe(It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task NullPaneDoesNotEscape()
    {
        var observed = new ConcurrentQueue<Exception>();
        var faultObserved = NewSignal();
        var sink = Create(
            action => Task.Run(action),
            (_, _) => null,
            exception =>
            {
                observed.Enqueue(exception);
                faultObserved.TrySetResult(null);
            });

        sink.WriteLine(
            (PathEx)@"C:\workspace",
            _ => Task.CompletedTask,
            new StringBuildMessage { Message = "build text", });
        await faultObserved.Task;

        observed.Should().ContainSingle();
    }

    private static BuildOutputSink Create(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane,
        Action<Exception> observeFault)
    {
        var constructor = typeof(BuildOutputSink).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(Func<Func<Task>, Task>),
                typeof(Func<Guid, string, IVsOutputWindowPane>),
                typeof(Action<Exception>),
            },
            null);
        constructor.Should().NotBeNull();
        return (BuildOutputSink)constructor.Invoke(
            new object[] { runOnMainThreadAsync, getOrCreatePane, observeFault, });
    }

    private static TaskCompletionSource<object> NewSignal()
    {
        return new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

[Trait("type", "UnitTests")]
public sealed class InstallToolchainCommandTests
{
    [Fact]
    public void FormatsExactInstallationStartedGuidance()
    {
        var formatter = typeof(InstallToolchainCommand).GetMethod(
            "FormatInstallationStartedMessage",
            BindingFlags.NonPublic | BindingFlags.Static);
        formatter.Should().NotBeNull();

        var message = formatter.Invoke(null, new object[] { "stable", });

        message.Should().Be(
            "Starting installation of toolchain 'stable'. See Output > Build for detailed status. Once done, you'll be notified here.");
    }
}
