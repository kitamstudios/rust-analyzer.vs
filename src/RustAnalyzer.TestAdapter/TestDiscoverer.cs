using System.Collections.Generic;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

[DefaultExecutorUri(Constants.ExecutorUriString)]
[FileExtension(Constants.ManifestExtension)]
public class TestDiscoverer : ITestDiscoverer
{
    public TestDiscoverer()
    {
        T = new TelemetryService();
    }

    public TelemetryService T { get; }

    public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
    }
}
