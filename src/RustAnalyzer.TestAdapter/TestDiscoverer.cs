using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        var testCaseInfos = (await FindTestsInSourceAsync(source, ct)).Select(t => CreateTestCaseFromTestCaseInfo(source, t));
        _t.TrackEvent("DiscoverTestsFromOneSource", ("Source", source), ("NumberOfTests", $"{testCaseInfos.Count()}"));

        return testCaseInfos;
    }

    private async Task DiscoverAndReportTestsFromOneSource(string source, ITestCaseDiscoverySink discoverySink, ILogger l)
    {
        try
        {
            l.WriteLine("Starting discovery of tests from {0}.", source);

            var testCaseInfos = await FindTestsInSourceAsync(source, default);
            _t.TrackEvent("DiscoverTestsFromOneSource", ("NumberOfTests", "{testCaseInfos.Count()}"));
            foreach (var testCaseInfo in testCaseInfos)
            {
                var testCase = CreateTestCaseFromTestCaseInfo(source, testCaseInfo);
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

    private static TestCase CreateTestCaseFromTestCaseInfo(string source, string testcaseInfo)
    {
        return new TestCase
        {
            CodeFilePath = source,
            LineNumber = 0,
            DisplayName = testcaseInfo,
            ExecutorUri = new Uri(Constants.ExecutorUriString),
            FullyQualifiedName = testcaseInfo,
            Source = source,
        };
    }

    private static async Task<IEnumerable<string>> FindTestsInSourceAsync(string source, CancellationToken ct)
    {
        if (!new FileInfo(source.ToUpperInvariant()).FullName.EndsWith(@"TestProjects\workspace_with_tests\adder\Cargo.toml".ToUpperInvariant()))
        {
            return Enumerable.Empty<string>();
        }

        var tests = new[]
        {
            "tests::it_works_failing",
            "tests::it_works_passing",
            "tests::it_works_skipped",
        }.AsEnumerable();

        return await Task.FromResult(tests);
    }
}
