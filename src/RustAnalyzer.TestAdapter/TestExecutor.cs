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

    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var tl = frameworkHandle.CreateTL();
        tl.L.WriteLine("RunTests starting. Executing {0} tests", tests.Count());
        var tasks = tests
            .GroupBy(t => t.Source)
            .Select(g => (Source: g.Key, Tests: g.AsEnumerable()))
            .Select(async x => await RunAndRecordTestResultsFromOneSourceAsync(await ((PathEx)x.Source).ReadTestContainerAsync(ct), x.Tests, runContext.IsBeingDebugged, frameworkHandle, tl, ct));
        Task.WaitAll(tasks.ToArray());
    }

    public override void RunTests(IEnumerable<PathEx> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var tl = frameworkHandle.CreateTL();
        tl.L.WriteLine("RunTests starting. Executing {0} sources.", sources.Count());
        var tasks = sources.Select(async source => await RunTestsTestsFromOneSourceAsync(await source.ReadTestContainerAsync(ct), runContext, frameworkHandle, tl, ct));
        Task.WaitAll(tasks.ToArray());
    }

    public static async Task RunTestsTestsFromOneSourceAsync(TestContainer tc, IRunContext runContext, IFrameworkHandle fh, TL tl, CancellationToken ct)
    {
        var testCases = await tc.DiscoverTestCasesFromOneSourceAsync(tl, ct);
        await RunAndRecordTestResultsFromOneSourceAsync(tc, testCases, runContext.IsBeingDebugged, fh, tl, ct);
    }

    private static async Task RunAndRecordTestResultsFromOneSourceAsync(TestContainer tc, IEnumerable<TestCase> testCases, bool isBeingDebugged, IFrameworkHandle fh, TL tl, CancellationToken ct)
    {
        try
        {
            var testResults = await RunTestsFromOneSourceAsync(tc, testCases, tl, isBeingDebugged, fh, ct);
            foreach (var testResult in testResults)
            {
                fh.RecordResult(testResult);
            }
        }
        catch (Exception e)
        {
            tl.L.WriteError("RunTests failed with {0}", e);
            tl.T.TrackException(e);
            throw;
        }
    }

    private static async Task<IEnumerable<TestResult>> RunTestsFromOneSourceAsync(TestContainer tc, IEnumerable<TestCase> testCases, TL tl, bool isBeingDebugged, IFrameworkHandle fh, CancellationToken ct)
    {
        tl.L.WriteLine("RunTestsFromOneSourceAsync starting with {0}", tc.ThisPath);
        if (!tc.IsTestDiscoveryComplete())
        {
            tl.L.WriteLine("RunTestsFromOneSourceAsync discovery not complete, starting discovery.");
            await tc.DiscoverTestCasesFromOneSourceAsync(tl, ct);
            tc = await tc.ThisPath.ReadTestContainerAsync(ct);
            tl.L.WriteLine("RunTestsFromOneSourceAsync rediscovery completed, test exe {0}.", tc.TestExe);
        }

        var args = testCases
                .Select(tc => tc.FullyQualifiedName.TestExplorerFQN2RustTestFQN())
                .Concat(new[] { "--format", "json", "-Zunstable-options", "--report-time" })
                .Concat(tc.AdditionalTestExecutionArguments.FromNullSeparatedArray())
                .ToArray();
        var envDict = tc.TestExecutionEnvironment.OverrideProcessEnvironment();
        tl.T.TrackEvent("RunTestsFromOneSourceAsync", ("IsBeingDebugged", $"{isBeingDebugged}"), ("Args", string.Join("|", args)), ("Env", tc.TestExecutionEnvironment.ReplaceNullWithBar()));
        if (isBeingDebugged)
        {
            tl.L.WriteLine("RunTestsFromOneSourceAsync launching test under debugger.");
            var rc = fh.LaunchProcessWithDebuggerAttached(tc.TestExe, tc.TestExe.GetDirectoryName(), string.Join(" ", args), envDict);
            if (rc != 0)
            {
                tl.L.WriteError("RunTestsFromOneSourceAsync launching test under debugger - returned {0}.", rc);
            }

            return Enumerable.Empty<TestResult>();
        }

        using var testExeProc = await ProcessRunner.RunWithLogging(tc.TestExe, args, tc.Manifest.GetDirectoryName(), envDict, ct, tl.L, @throw: false);
        var ec = testExeProc.ExitCode ?? 0;
        if (ec != 0)
        {
            tl.L.WriteError("RunTestsFromOneSourceAsync test executable exited with code {0}.", ec);
        }

        var testCasesMap = testCases.ToImmutableDictionary(x => x.FullyQualifiedName.TestExplorerFQN2RustTestFQN());
        var tris = testExeProc.StandardOutputLines
            .Skip(1)
            .Take(testExeProc.StandardOutputLines.Count() - 2)
            .Select(JsonConvert.DeserializeObject<TestRunInfo>)
            .Where(x => x.Event != TestRunInfo.EventType.Started)
            .OrderBy(x => x.FQN)
            .Select(x => ToTestResult(x, testCasesMap));

        tl.T.TrackEvent("RunTestsFromOneSourceAsync", ("Results", $"{tris.Count()}"));

        return tris;
    }

    private static TestResult ToTestResult(TestRunInfo tri, IReadOnlyDictionary<string, TestCase> testCasesMap)
    {
        return new TestResult(testCasesMap[tri.FQN])
        {
            DisplayName = tri.FQN.RustTestFQN2TestExplorerFQN(),
            ErrorMessage = string.Join("\n", new[] { tri.Message, tri.StdOut }.Where(x => !string.IsNullOrEmpty(x))),
            Outcome = GetOutcome(tri.Event),
            Duration = TimeSpan.FromSeconds(tri.ExecutionTime)
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
