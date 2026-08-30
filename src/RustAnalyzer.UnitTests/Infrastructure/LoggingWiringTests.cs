using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.Shell;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class OutputWindowLoggerTests
{
    [Fact]
    public async Task CreatesPaneLazilyReusesItAndRoutesNormalAndErrorMessagesAsync()
    {
        var writes = new ConcurrentQueue<string>();
        var firstWrite = NewSignal();
        var secondWrite = NewSignal();
        var writeCount = 0;
        var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
        pane.Setup(value => value.OutputStringThreadSafe(It.IsAny<string>()))
            .Callback(
                (string value) =>
                {
                    writes.Enqueue(value);
                    if (Interlocked.Increment(ref writeCount) == 1)
                    {
                        firstWrite.TrySetResult(null);
                    }
                    else
                    {
                        secondWrite.TrySetResult(null);
                    }
                })
            .Returns(VSConstants.S_OK);
        var paneAcquisitions = 0;
        var faults = new ConcurrentQueue<Exception>();
        var logger = Create<OutputWindowLogger>(
            action => Task.Run(action),
            (_, _) =>
            {
                Interlocked.Increment(ref paneAcquisitions);
                return pane.Object;
            },
            faults.Enqueue);

        paneAcquisitions.Should().Be(0);
        logger.WriteLine("value {0}", 7);
        await firstWrite.Task;
        logger.WriteError("failure {0}", 8);
        await secondWrite.Task;

        paneAcquisitions.Should().Be(1);
        writes.Should().HaveCount(2);
        writes.ElementAt(0).Should().EndWith(" - value 7\n");
        writes.ElementAt(1).Should().EndWith(" - [ERROR]: failure 8\n");
        faults.Should().BeEmpty();
        pane.Verify(
            value => value.OutputStringThreadSafe(It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ObservesWriteFailureOnceAsync()
    {
        var observed = new ConcurrentQueue<Exception>();
        var faultObserved = NewSignal();
        var pane = new Mock<IVsOutputWindowPane>(MockBehavior.Strict);
        pane.Setup(value => value.OutputStringThreadSafe(It.IsAny<string>()))
            .Returns(VSConstants.E_FAIL);
        var logger = Create<OutputWindowLogger>(
            action => Task.Run(action),
            (_, _) => pane.Object,
            exception =>
            {
                observed.Enqueue(exception);
                faultObserved.TrySetResult(null);
            });

        logger.WriteLine("message");
        await faultObserved.Task;

        observed.Should().ContainSingle();
    }

    private static T Create<T>(
        Func<Func<Task>, Task> runOnMainThreadAsync,
        Func<Guid, string, IVsOutputWindowPane> getOrCreatePane,
        Action<Exception> observeFault)
    {
        var constructor = typeof(T).GetConstructor(
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
        return (T)constructor.Invoke(
            new object[] { runOnMainThreadAsync, getOrCreatePane, observeFault, });
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
