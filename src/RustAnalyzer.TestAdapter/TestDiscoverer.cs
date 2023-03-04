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
[FileExtension(Constants.ManifestExtension)]
public class TestDiscoverer : ITestDiscoverer
{
    private readonly TelemetryService _t = new ();

    public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
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

    public async Task<IEnumerable<TestCase>> DiscoverTestCasesFromOneSourceAsync(string source, ILogger l, CancellationToken ct)
    {
        l.WriteLine("Starting discovery of tests from {0}.", source);

        var testCaseInfos = (await FindTestsInSourceAsync(source, l, ct)).Select(t => CreateTestCaseFromTest(source, t));
        _t.TrackEvent("DiscoverTestsFromOneSource", ("Source", source), ("NumberOfTests", $"{testCaseInfos.Count()}"));

        return testCaseInfos;
    }

    private async Task DiscoverAndReportTestsFromOneSource(string source, ITestCaseDiscoverySink discoverySink, ILogger l)
    {
        try
        {
            l.WriteLine("Starting discovery of tests from {0}.", source);

            var testCaseInfos = await FindTestsInSourceAsync(source, l, default);
            _t.TrackEvent("DiscoverTestsFromOneSource", ("NumberOfTests", "{testCaseInfos.Count()}"));
            foreach (var testCaseInfo in testCaseInfos)
            {
                var testCase = CreateTestCaseFromTest(source, testCaseInfo);
                discoverySink.SendTestCase(testCase);
            }
        }
        catch (Exception e)
        {
            l.WriteError("DiscoverTestsFromOneSource failed with {0}", e);
            _t.TrackException(e, new[] { ("Source", source) });
            throw;
        }

        await Task.CompletedTask;
    }

    private static TestCase CreateTestCaseFromTest(string source, TestInfo test)
    {
        var fqnParts = test.FQN.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        return new TestCase
        {
            CodeFilePath = test.SourcePath,
            LineNumber = test.StartLine,
            DisplayName = fqnParts[fqnParts.Length - 1],
            ExecutorUri = new Uri(Constants.ExecutorUriString),
            FullyQualifiedName = string.Join(".", fqnParts),
            Source = source,
        };
    }

    private Task<IEnumerable<TestInfo>> FindTestsInSourceAsync(string source, ILogger l, CancellationToken ct)
    {
        return new ToolChainService(_t, l).GetTestSuiteAsync((PathEx)source, ct);
    }
}
