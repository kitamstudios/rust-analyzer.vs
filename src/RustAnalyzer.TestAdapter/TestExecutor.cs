using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace KS.RustAnalyzer.TestAdapter;

[ExtensionUri(Constants.ExecutorUriString)]
public class TestExecutor : ITestExecutor
{
    private readonly TelemetryService _t = new ();

    private bool _cancelled;

    public void Cancel()
    {
        _cancelled = true;
    }

    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var l = new TestAdapterLogger(frameworkHandle);
        try
        {
            var tasks = tests
                .GroupBy(t => t.Source)
                .Select(g => (Source: g.Key, Tests: g.AsEnumerable()))
                .Select(x => RunAndRecordTestResultsFromOneSourceAsync(x.Source, x.Tests, frameworkHandle, l, _t, ct));
            Task.WaitAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            l.WriteError("RunTests failed with {0}", e);
            _t.TrackException(e);
            throw;
        }
    }

    public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var l = new TestAdapterLogger(frameworkHandle);
        try
        {
            var tasks = sources.Select(source => RunTestsTestsFromOneSourceAsync(source, runContext, frameworkHandle, l, _t, ct));
            Task.WaitAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            l.WriteError("RunTests failed with {0}", e);
            _t.TrackException(e);
            throw;
        }
    }

    public static async Task RunTestsTestsFromOneSourceAsync(string source, IRunContext runContext, IFrameworkHandle frameworkHandle, ILogger l, ITelemetryService t, CancellationToken ct)
    {
        var discoverer = new TestDiscoverer();
        var testCases = await discoverer.DiscoverTestCasesFromOneSourceAsync(source, l, ct);
        await RunAndRecordTestResultsFromOneSourceAsync(source, testCases, frameworkHandle, l, t, ct);
    }

    private static async Task RunAndRecordTestResultsFromOneSourceAsync(string source, IEnumerable<TestCase> testCases, IFrameworkHandle frameworkHandle, ILogger l, ITelemetryService t, CancellationToken ct)
    {
        var testResults = await RunTestsFromOneSourceAsync(source, testCases, l, t, ct);
        foreach (var testResult in testResults)
        {
            frameworkHandle.RecordResult(testResult);
        }
    }

    private static Task<IEnumerable<TestResult>> RunTestsFromOneSourceAsync(string source, IEnumerable<TestCase> testCases, ILogger l, ITelemetryService t, CancellationToken ct)
    {
        var testResults = testCases.Select(t => new TestResult(t)
        {
            Outcome = t.FullyQualifiedName.IndexOf("fail", StringComparison.OrdinalIgnoreCase) <= 0 ? TestOutcome.Passed : TestOutcome.Failed,
        });

        t.TrackEvent("RunTestsFromOneSourceAsync", ("Results", $"{testResults.Count()}"));

        return Task.FromResult(testResults);
    }
}
