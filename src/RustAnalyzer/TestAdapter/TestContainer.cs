using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.TestWindow.Extensibility.Model;

namespace KS.RustAnalyzer.TestAdapter;

public class TestContainer : ITestContainer
{
    public ITestContainerDiscoverer Discoverer => throw new NotImplementedException();

    public string Source => throw new NotImplementedException();

    public IEnumerable<Guid> DebugEngines => throw new NotImplementedException();

    public FrameworkVersion TargetFramework => throw new NotImplementedException();

    public Architecture TargetPlatform => throw new NotImplementedException();

    public bool IsAppContainerTestContainer => throw new NotImplementedException();

    public int CompareTo(ITestContainer other)
    {
        throw new NotImplementedException();
    }

    public IDeploymentData DeployAppContainer()
    {
        throw new NotImplementedException();
    }

    public ITestContainer Snapshot()
    {
        throw new NotImplementedException();
    }
}