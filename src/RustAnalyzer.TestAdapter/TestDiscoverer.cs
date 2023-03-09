using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

[DefaultExecutorUri(Constants.ExecutorUriString)]
[FileExtension(Constants.TestsContainerExtension)]
public class TestDiscoverer : BaseTestDiscoverer, ITestDiscoverer
{
    public override void DiscoverTests(IEnumerable<PathEx> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        var tl = logger.CreateTL();
        try
        {
            var tasks = sources.Select(source => DiscoverAndReportTestsFromOneSource(source, discoverySink, tl, default));
            Task.WaitAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            tl.L.WriteError("DiscoverTests failed with {0}", e);
            tl.T.TrackException(e);
            throw;
        }
    }

    private async Task DiscoverAndReportTestsFromOneSource(PathEx source, ITestCaseDiscoverySink discoverySink, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("DiscoverAndReportTestsFromOneSource starting with {0}", source);
        try
        {
            var testCaseInfos = await source.DiscoverTestCasesFromOneSourceAsync(tl, ct);
            testCaseInfos.ForEach(discoverySink.SendTestCase);
        }
        catch (Exception e)
        {
            tl.L.WriteError("DiscoverAndReportTestsFromOneSource failed with {0}", e);
            tl.T.TrackException(e, new[] { ("Source", $"{source}") });
            throw;
        }

        await Task.CompletedTask;
    }
}
