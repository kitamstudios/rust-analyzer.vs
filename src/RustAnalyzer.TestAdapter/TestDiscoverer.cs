using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

/// <summary>
/// Discovery of tests happen by:
/// 1. Fetching exes for each test container in parallel.
/// 2. Running all exes in paralle.
/// </summary>
[DefaultExecutorUri(Constants.ExecutorUriString)]
[FileExtension(Constants.TestsContainerExtension)]
public class TestDiscoverer : BaseTestDiscoverer, ITestDiscoverer
{
    public override void DiscoverTests(IEnumerable<PathEx> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        var tl = logger.CreateTL();
        var tasks = sources
            .GroupBy(s => s)
            .Select(async g => await DiscoverAndReportTestsFromOneSource(await g.Key.ReadTestContainerAsync(default), discoverySink, tl, default));
        Task.WaitAll(tasks.ToArray());
    }

    /// <summary>
    /// Each TestContainer contains multiple Exes, each Exes contain multiple tests.
    /// </summary>
    private async Task DiscoverAndReportTestsFromOneSource(TestContainer tc, ITestCaseDiscoverySink discoverySink, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("DiscoverAndReportTestsFromOneSource starting with {0}", tc.ThisPath);
        try
        {
            foreach (var (_, tcs) in await tc.DiscoverTestCasesFromOneSourceAsync(tl, ct))
            {
                tcs.ForEach(discoverySink.SendTestCase);
            }
        }
        catch (Exception e)
        {
            tl.L.WriteError("DiscoverAndReportTestsFromOneSource failed with {0}", e);
            tl.T.TrackException(e, new[] { ("Source", $"{tc.ThisPath}") });
            throw;
        }
    }
}
