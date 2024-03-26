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

/// <summary>
/// Execution of tests happen by running an exe belonging to a test container.
///
/// By the time we are here, it is fine to assume that TestContainer is complete (i.e. filled with Exes).
/// </summary>
[ExtensionUri(Constants.ExecutorUriString)]
public class TestExecutor : BaseTestExecutor, ITestExecutor
{
    private bool _cancelled;

    /// <summary>
    /// Signature requried by ITestExecutor.
    /// </summary>
    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        var tl = frameworkHandle.CreateTL();
        tl.L.WriteLine("RunTests starting. Executing {0} tests", tests.Count());
        var tasks = tests
            .GroupBy(t => t.Source)
            .Select(g => (g.Key, g.AsEnumerable()))
            .Select(async g => (await ((PathEx)g.Item1).ReadTestContainerAsync(ct), g.Item2))
            .Select(async x =>
            {
                var (c, tcs) = await x;
                var ts = c.TestExes.Select(async exe => await RunAndRecordTestResultsFromOneExe(exe, tcs, TestRunParams.FromContainer(c), runContext.IsBeingDebugged, frameworkHandle, tl, ct));
                Task.WaitAll(ts.ToArray());
            });

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

    /// <summary>
    /// Each TestContainer has multiple Exes, Each exe has multiple tests.
    /// Execution of tests happen by running the Exes. All in parallel.
    /// </summary>
    public static async Task RunTestsTestsFromOneSourceAsync(TestContainer container, IRunContext runContext, IFrameworkHandle fh, TL tl, CancellationToken ct)
    {
        foreach (var (tsi, tcs) in await container.DiscoverTestCasesFromOneSourceAsync(tl, ct))
        {
            await RunAndRecordTestResultsFromOneExe(tsi.Exe, tcs, TestRunParams.FromContainer(container), runContext.IsBeingDebugged, fh, tl, ct);
        }
    }

    public void Cancel()
    {
        _cancelled = true;
    }

    private static async Task RunAndRecordTestResultsFromOneExe(PathEx exe, IEnumerable<TestCase> testCases, TestRunParams trp, bool isBeingDebugged, IFrameworkHandle fh, TL tl, CancellationToken ct)
    {
        try
        {
            var testResults = await RunTestsFromOneExe(exe, testCases, trp, tl, isBeingDebugged, fh, ct);
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

    private static async Task<IEnumerable<TestResult>> RunTestsFromOneExe(PathEx exe, IEnumerable<TestCase> testCases, TestRunParams trp, TL tl, bool isBeingDebugged, IFrameworkHandle fh, CancellationToken ct)
    {
        tl.L.WriteLine("RunTestsFromOneExe starting with {0}, {1}", trp.Source, exe);
        if (!testCases.Any())
        {
            tl.L.WriteError("RunTestsFromOneSourceAsync: Something has gone wrong. Asking to run empty set of test cases. {0}, {1}", trp.Source, exe);
        }

        var args = testCases
                .Select(tc => tc.FullyQualifiedNameRustFormat())
                .Concat(new[] { "--exact", "--format", "json", "-Zunstable-options", "--report-time" })
                .Concat(trp.AdditionalTestExecutionArguments.FromNullSeparatedArray())
                .ToArray();
        var envDict = trp.TestExecutionEnvironment.OverrideProcessEnvironment();
        tl.T.TrackEvent("RunTestsFromOneSourceAsync", ("IsBeingDebugged", $"{isBeingDebugged}"), ("Args", string.Join("|", args)), ("Env", trp.TestExecutionEnvironment.ReplaceNullWithBar()));
        if (isBeingDebugged)
        {
            tl.L.WriteLine("RunTestsFromOneSourceAsync launching test under debugger.");
            var rc = fh.LaunchProcessWithDebuggerAttached(exe, exe.GetDirectoryName(), string.Join(" ", args), envDict);
            if (rc != 0)
            {
                tl.L.WriteError("RunTestsFromOneSourceAsync launching test under debugger - returned {0}.", rc);
            }

            return Enumerable.Empty<TestResult>();
        }

        using var testExeProc = await ProcessRunner.RunWithLogging(exe, args, exe.GetDirectoryName(), envDict, ct, tl.L, @throw: false);

        var testCasesMap = testCases.ToImmutableDictionary(x => x.FullyQualifiedNameRustFormat());
        var tris = testExeProc.StandardOutputLines
            .Skip(1)
            .Take(testExeProc.StandardOutputLines.Count() - 2)
            .Select(JsonConvert.DeserializeObject<TestRunInfo>)
            .Where(x => x.Event != TestRunInfo.EventType.Started)
            .OrderBy(x => x.FQN)
            .Select(x => ToTestResult(exe, x, testCasesMap));
        var ec = testExeProc.ExitCode ?? 0;
        if (ec != 0 && !tris.Any())
        {
            tl.L.WriteError("RunTestsFromOneSourceAsync test executable exited with code {0}.", ec);
            throw new ApplicationException($"Test executable returned {ec}. Check above for the arguments passed to test executable by running it on the command line.");
        }

        tl.T.TrackEvent("RunTestsFromOneSourceAsync", ("Results", $"{tris.Count()}"));

        return tris;
    }

    private static TestResult ToTestResult(PathEx exe, TestRunInfo tri, IReadOnlyDictionary<string, TestCase> testCasesMap)
    {
        return new TestResult(testCasesMap[tri.FQN])
        {
            DisplayName = tri.FQN.RustFQN2TestExplorerFQN(exe),
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

    public class TestRunParams
    {
        public PathEx Source { get; set; }

        public string AdditionalTestExecutionArguments { get; set; }

        public string TestExecutionEnvironment { get; set; }

        public static TestRunParams FromContainer(TestContainer tc)
        {
            return new TestRunParams
            {
                Source = tc.ThisPath,
                AdditionalTestExecutionArguments = tc.AdditionalTestExecutionArguments,
                TestExecutionEnvironment = tc.TestExecutionEnvironment,
            };
        }
    }
}
