using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter;

[ExtensionUri(Constants.ExecutorUriString)]
public class TestExecutor : BaseTestExecutor, ITestExecutor
{
    private bool _cancelled;

    public void Cancel()
    {
        _cancelled = true;
    }

    // TODO: Unit tests for this.
    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var tl = frameworkHandle.CreateTL();
        try
        {
            var tasks = tests
                .GroupBy(t => t.Source)
                .Select(g => (Source: g.Key, Tests: g.AsEnumerable()))
                .Select(x => RunAndRecordTestResultsFromOneSourceAsync((PathEx)x.Source, x.Tests, frameworkHandle, tl, ct));
            Task.WaitAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            tl.L.WriteError("RunTests failed with {0}", e);
            tl.T.TrackException(e);
            throw;
        }
    }

    public override void RunTests(IEnumerable<PathEx> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var tl = frameworkHandle.CreateTL();
        try
        {
            var tasks = sources.Select(source => RunTestsTestsFromOneSourceAsync(source, runContext, frameworkHandle, tl, ct));
            Task.WaitAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            tl.L.WriteError("RunTests failed with {0}", e);
            tl.T.TrackException(e);
            throw;
        }
    }

    public static async Task RunTestsTestsFromOneSourceAsync(PathEx source, IRunContext runContext, IFrameworkHandle frameworkHandle, TL tl, CancellationToken ct)
    {
        var testCases = await source.DiscoverTestCasesFromOneSourceAsync(tl, ct);
        await RunAndRecordTestResultsFromOneSourceAsync(source, testCases, frameworkHandle, tl, ct);
    }

    private static async Task RunAndRecordTestResultsFromOneSourceAsync(PathEx source, IEnumerable<TestCase> testCases, IFrameworkHandle frameworkHandle, TL tl, CancellationToken ct)
    {
        var testResults = await RunTestsFromOneSourceAsync(source, testCases, tl, ct);
        foreach (var testResult in testResults)
        {
            frameworkHandle.RecordResult(testResult);
        }
    }

    // TODO: Unable to navigate to test in superworkspace add_one/add_one issue.
    // TODO: Enable CDP integration tests.
    private static async Task<IEnumerable<TestResult>> RunTestsFromOneSourceAsync(PathEx source, IEnumerable<TestCase> testCases, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("RunTestsFromOneSourceAsync starting with {0}", source);
        var tc = JsonConvert.DeserializeObject<TestContainer>(await source.ReadAllTextAsync(ct));
        if (!tc.IsTestDiscoveryComplete())
        {
            tl.L.WriteLine("RunTestsFromOneSourceAsync discovery not complete, starting discovery.");
            await source.DiscoverTestCasesFromOneSourceAsync(tl, ct);
            tc = JsonConvert.DeserializeObject<TestContainer>(await source.ReadAllTextAsync(ct));
            tl.L.WriteLine("RunTestsFromOneSourceAsync rediscovery completed, test exe {0}.", tc.TestExe);
        }

        using var testExeProc = ProcessRunner.Run(tc.TestExe, new[] { "--format", "json", "-Zunstable-options" }, ct);
        tl.L.WriteLine("Started PID:{0}, with args: {1}...", testExeProc.ProcessId, testExeProc.Arguments);
        var testExeExitCode = await testExeProc;
        tl.L.WriteLine("... Finished PID {0} with exit code {1}.", testExeProc.ProcessId, testExeProc.ExitCode);

        if (testExeExitCode != 0)
        {
            // TODO: Handle non 0 return codes
            // throw new InvalidOperationException($"{testExeExitCode}\n{string.Join("\n", testExeProc.StandardErrorLines)}");
        }

        var testCasesMap = testCases.ToImmutableDictionary(x => x.FullyQualifiedName.Replace(".", "::"));
        var tris = testExeProc.StandardOutputLines
            .Skip(1)
            .Take(testExeProc.StandardOutputLines.Count() - 2)
            .Select(JsonConvert.DeserializeObject<TestRunInfo>)
            .Where(x => x.Event != TestRunInfo.EventType.Started) // TODO: Need to implement test timing infomation.
            .OrderBy(x => x.FQN)
            .Select(x => ToTestResult(x, testCasesMap));

        tl.T.TrackEvent("RunTestsFromOneSourceAsync", ("Results", $"{tris.Count()}"));

        return tris;
    }

    private static TestResult ToTestResult(TestRunInfo tri, IReadOnlyDictionary<string, TestCase> testCasesMap)
    {
        return new TestResult(testCasesMap[tri.FQN])
        {
            DisplayName = tri.FQN,
            ErrorMessage = tri.StdOut,
            Outcome = GetOutcome(tri.Event),
        };
    }

    private static TestOutcome GetOutcome(TestRunInfo.EventType @event)
    {
        switch (@event)
        {
            case TestRunInfo.EventType.Started:
                return TestOutcome.None;
            case TestRunInfo.EventType.Ok:
                return TestOutcome.Passed;
            case TestRunInfo.EventType.Failed:
                return TestOutcome.Failed;
            case TestRunInfo.EventType.Ignored:
                return TestOutcome.Skipped;
            default:
                return TestOutcome.None;
        }
    }
}
