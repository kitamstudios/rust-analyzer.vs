using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.TestWindow.Extensibility.Model;

namespace KS.RustAnalyzer.TestAdapter;

[DebuggerDisplay("{Constants.ExecutorUriString}/{Source}")]
public class TestContainer : BaseTestContainer, ITestContainer
{
    public TestContainer(PathEx testContainerPath, ITestContainerDiscoverer discoverer, TL tl)
    {
        EnsureArg.IsTrue(testContainerPath.FileExists(), nameof(testContainerPath));
        TestContainerPath = testContainerPath;
        TimeStamp = GetTimeStamp();
        Discoverer = discoverer;
        TL = tl;

        TL.T.TrackEvent("NewTestContainer", ("ManifestPath", testContainerPath));
        TL.L.WriteLine("New Test container {0} [{1}]", testContainerPath, TimeStamp);
    }

    private TestContainer(TestContainer testContainer)
        : this(testContainer.TestContainerPath, testContainer.Discoverer, testContainer.TL)
    {
        TimeStamp = testContainer.TimeStamp;
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

        TL.L.WriteLine("Test container comparision {0} vs {1} for {2}", TimeStamp, otherContainer.TimeStamp, TestContainerPath);

        return TimeStamp.CompareTo(otherContainer.TimeStamp);
    }

    public IDeploymentData DeployAppContainer() => null;

    public ITestContainer Snapshot() => new TestContainer(this);

    private DateTime GetTimeStamp()
    {
        if (TestContainerPath.FileExists())
        {
            return File.GetLastWriteTime(TestContainerPath);
        }

        return DateTime.MinValue;
    }
}
