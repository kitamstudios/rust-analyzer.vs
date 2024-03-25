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

    private async Task DiscoverAndReportTestsFromOneSource(TestContainer tc, ITestCaseDiscoverySink discoverySink, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("DiscoverAndReportTestsFromOneSource starting with {0}", tc.ThisPath);
        try
        {
            var testCaseInfos = await tc.DiscoverTestCasesFromOneSourceAsync(tl, ct);
            testCaseInfos.ForEach(discoverySink.SendTestCase);
        }
        catch (Exception e)
        {
            tl.L.WriteError("DiscoverAndReportTestsFromOneSource failed with {0}", e);
            tl.T.TrackException(e, new[] { ("Source", $"{tc.ThisPath}") });
            throw;
        }

        await Task.CompletedTask;
    }
}
