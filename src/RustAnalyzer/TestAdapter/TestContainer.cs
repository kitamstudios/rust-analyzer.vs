using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.TestWindow.Extensibility.Model;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

[DebuggerDisplay("{Constants.ExecutorUriString}/{Source}")]
public class TestContainer : ITestContainer
{
    public TestContainer(string source, ITestContainerDiscoverer discoverer, ILogger l, ITelemetryService t)
    {
        Source = source;
        TimeStamp = GetTimeStamp();
        Discoverer = discoverer;
        L = l;
        T = t;

        T.TrackEvent("NewTestContainer", ("Source", source));
        L.WriteLine("New Test container {0} [{1}]", source, TimeStamp);
    }

    private TestContainer(TestContainer testContainer)
        : this(testContainer.Source, testContainer.Discoverer, testContainer.L, testContainer.T)
    {
        TimeStamp = testContainer.TimeStamp;
    }

    public ITestContainerDiscoverer Discoverer { get; }

    public string Source { get; }

    public IEnumerable<Guid> DebugEngines => Enumerable.Empty<Guid>();

    public FrameworkVersion TargetFramework => FrameworkVersion.None;

    public Architecture TargetPlatform => Architecture.Default;

    public bool IsAppContainerTestContainer => false;

    public DateTime TimeStamp { get; }

    public ILogger L { get; }

    public ITelemetryService T { get; }

    public int CompareTo(ITestContainer other)
    {
        if (other is not TestContainer otherContainer)
        {
            return -1;
        }

        var res = string.Compare(Source, otherContainer.Source);
        if (res != 0)
        {
            return res;
        }

        L.WriteLine("Test container comparision {0} vs {1} for {2}", TimeStamp, otherContainer.TimeStamp, Source);

        return TimeStamp.CompareTo(otherContainer.TimeStamp);
    }

    public IDeploymentData DeployAppContainer() => null;

    public ITestContainer Snapshot() => new TestContainer(this);

    private DateTime GetTimeStamp()
    {
        if (File.Exists(Source))
        {
            return File.GetLastWriteTime(Source);
        }

        return DateTime.MinValue;
    }
}
