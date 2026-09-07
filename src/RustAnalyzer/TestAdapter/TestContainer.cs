using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.TestWindow.Extensibility.Model;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

[DebuggerDisplay("{Constants.ExecutorUriString}/{Source}")]
public class TestContainer : BaseTestContainer, ITestContainer
{
    private readonly MelLogger _logger;

    public TestContainer(PathEx testContainerPath, ITestContainerDiscoverer discoverer, TL tl)
        : this(
            testContainerPath,
            discoverer,
            tl,
            LegacyLoggerBridge.ToMelLogger(tl.L))
    {
    }

    public TestContainer(
        PathEx testContainerPath,
        ITestContainerDiscoverer discoverer,
        TL tl,
        MelLogger logger)
    {
        EnsureArg.IsTrue(testContainerPath.FileExists(), nameof(testContainerPath));
        TestContainerPath = testContainerPath;
        TimeStamp = GetTimeStamp();
        Discoverer = discoverer;
        TL = tl;
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));

        _logger.LogInformation(
            new EventId(1, "ContainerCreated"),
            "New Test container {TestContainerPath} [{Timestamp}]",
            testContainerPath,
            TimeStamp);
    }

    private TestContainer(TestContainer testContainer)
        : this(
            testContainer.TestContainerPath,
            testContainer.Discoverer,
            testContainer.TL,
            testContainer._logger)
    {
    }

    public override PathEx TestContainerPath { get; }

    public ITestContainerDiscoverer Discoverer { get; }

    public IEnumerable<Guid> DebugEngines => new[] { VSConstants.DebugEnginesGuids.NativeOnly_guid };

    public FrameworkVersion TargetFramework => FrameworkVersion.None;

    public Architecture TargetPlatform => Architecture.Default;

    public bool IsAppContainerTestContainer => false;

    public DateTime TimeStamp { get; }

    public TL TL { get; }

    public int CompareTo(ITestContainer other)
    {
        if (other is not TestContainer otherContainer)
        {
            return -1;
        }

        var res = string.Compare(TestContainerPath, otherContainer.TestContainerPath);
        if (res != 0)
        {
            return res;
        }

        _logger.LogInformation(
            new EventId(2, "ContainerCompared"),
            "Test container comparision {Timestamp} vs {OtherTimestamp} for {TestContainerPath}",
            TimeStamp,
            otherContainer.TimeStamp,
            TestContainerPath);

        return TimeStamp.CompareTo(otherContainer.TimeStamp);
    }

    public IDeploymentData DeployAppContainer() => null;

    public ITestContainer Snapshot() => File.Exists(TestContainerPath) ? new TestContainer(this) : null;

    private DateTime GetTimeStamp()
    {
        if (TestContainerPath.FileExists())
        {
            return File.GetLastWriteTime(TestContainerPath);
        }

        return DateTime.MinValue;
    }
}
