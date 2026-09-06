using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
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
    private readonly IFeatureUsageTelemetry _telemetry;

    public TestDiscoverer()
        : this(FeatureUsageTelemetry.CreateForTestAdapter())
    {
    }

    public TestDiscoverer(IFeatureUsageTelemetry telemetry)
    {
        _telemetry = EnsureArg.IsNotNull(telemetry, nameof(telemetry));
    }

    public override void DiscoverTests(IEnumerable<PathEx> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        var duration = Stopwatch.StartNew();
        using var invocationLogger = new TestAdapterLogger(logger);
        var tl = new TL { T = _telemetry, L = invocationLogger, };
        try
        {
            var tasks = sources
                .GroupBy(s => s)
                .Select(async g => await DiscoverAndReportTestsFromOneSource(await g.Key.ReadTestContainerAsync(default), discoverySink, tl, default));
            Task.WaitAll(tasks.ToArray());
            _telemetry.Track(UsageOperation.TestAdapterDiscover, UsageOutcome.Succeeded, duration.Elapsed);
        }
        catch (Exception e)
        {
            _telemetry.Track(
                UsageOperation.TestAdapterDiscover,
                IsCancellation(e) ? UsageOutcome.Cancelled : UsageOutcome.Failed,
                duration.Elapsed);
            throw;
        }
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
            throw;
        }
    }

    private static bool IsCancellation(Exception exception)
    {
        return exception is OperationCanceledException
            || (exception is AggregateException aggregate
                && aggregate.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException));
    }
}
