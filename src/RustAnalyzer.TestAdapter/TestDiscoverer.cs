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
    private readonly TelemetryService _t = new ();

    public override void DiscoverTests(IEnumerable<PathEx> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        var l = new TestAdapterLogger(logger);
        try
        {
            var tasks = sources.Select(source => DiscoverAndReportTestsFromOneSource(source, discoverySink, l));
            Task.WaitAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            l.WriteError("DiscoverTestsFromOneSource failed with {0}", e);
            _t.TrackException(e);
            throw;
        }
    }

    public async Task<IEnumerable<TestCase>> DiscoverTestCasesFromOneSourceAsync(PathEx source, ILogger l, CancellationToken ct)
    {
        l.WriteLine("Starting discovery of tests from {0}.", source);

        var testSuite = await FindTestsInSourceAsync(source, l, ct);
        var testCaseInfos = testSuite.Tests.Select(t => CreateTestCaseFromTest(testSuite.Container, t));
        _t.TrackEvent("DiscoverTestsFromOneSource", ("Source", source), ("NumberOfTests", $"{testCaseInfos.Count()}"));

        return testCaseInfos;
    }

    // TODO: DRY with previous method.
    private async Task DiscoverAndReportTestsFromOneSource(PathEx source, ITestCaseDiscoverySink discoverySink, ILogger l)
    {
        try
        {
            l.WriteLine("Starting discovery of tests from {0}.", source);

            var testSuite = await FindTestsInSourceAsync(source, l, default);
            _t.TrackEvent("DiscoverTestsFromOneSource", ("NumberOfTests", "{testCaseInfos.Count()}"));
            foreach (var testCaseInfo in testSuite.Tests)
            {
                var testCase = CreateTestCaseFromTest(testSuite.Container, testCaseInfo);
                discoverySink.SendTestCase(testCase);
            }
        }
        catch (Exception e)
        {
            l.WriteError("DiscoverTestsFromOneSource failed with {0}", e);
            _t.TrackException(e, new[] { ("Source", $"{source}") });
            throw;
        }

        await Task.CompletedTask;
    }

    private static TestCase CreateTestCaseFromTest(PathEx container, TestSuiteInfo.TestInfo test)
    {
        var fqnParts = test.FQN.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        return new TestCase
        {
            CodeFilePath = test.SourcePath,
            LineNumber = test.StartLine,
            DisplayName = fqnParts[fqnParts.Length - 1],
            ExecutorUri = new Uri(Constants.ExecutorUriString),
            FullyQualifiedName = string.Join(".", fqnParts),
            Source = container,
        };
    }

    private Task<TestSuiteInfo> FindTestsInSourceAsync(PathEx source, ILogger l, CancellationToken ct)
    {
        // TODO: remove hard coding.
        return new ToolChainService(_t, l).GetTestSuiteInfoAsync(source, "dev", ct);
    }
}
