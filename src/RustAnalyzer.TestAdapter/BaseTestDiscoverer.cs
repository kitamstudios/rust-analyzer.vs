using System.Collections.Generic;
using System.Linq;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public abstract class BaseTestDiscoverer
{
    public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        DiscoverTests(sources.Select(s => (PathEx)s).Where(s => s != default), discoveryContext, logger, discoverySink);
    }

    public abstract void DiscoverTests(IEnumerable<PathEx> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink);
}
