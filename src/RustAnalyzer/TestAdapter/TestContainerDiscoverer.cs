using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.TestWindow.Extensibility;

namespace KS.RustAnalyzer.TestAdapter;

[Export(typeof(ITestContainerDiscoverer))]
public class TestContainerDiscoverer : ITestContainerDiscoverer
{
    public event EventHandler TestContainersUpdated;

    public Uri ExecutorUri => new (Constants.ExecutorUriString);

    public IEnumerable<ITestContainer> TestContainers => Enumerable.Empty<ITestContainer>();

    public void OnTestContainersChanged()
    {
        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }
}
