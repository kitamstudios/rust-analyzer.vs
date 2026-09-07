using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Newtonsoft.Json;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

/// <summary>
/// Execution of tests happen by running an exe belonging to a test container.
///
/// By the time we are here, it is fine to assume that TestContainer is complete (i.e. filled with Exes).
/// </summary>
[ExtensionUri(Constants.ExecutorUriString)]
public class TestExecutor : BaseTestExecutor, ITestExecutor
{
    private readonly IFeatureUsageTelemetry _telemetry;
    private bool _cancelled;

    public TestExecutor()
        : this(FeatureUsageTelemetry.CreateForTestAdapter())
    {
    }

    public TestExecutor(IFeatureUsageTelemetry telemetry)
    {
        _telemetry = EnsureArg.IsNotNull(telemetry, nameof(telemetry));
    }

    /// <summary>
    /// Signature requried by ITestExecutor.
    /// </summary>
    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        using var loggingContext =
            new VSTestLoggingContext(frameworkHandle, "Execution");
        var executorLogger =
            loggingContext.CreateLogger(typeof(TestExecutor));
        var tl = new TL { T = _telemetry, L = loggingContext.LegacyLogger, };
        RunWithTelemetry(
            () =>
            {
                executorLogger.LogInformation(
                    new EventId(1, "SelectedTestExecutionStarted"),
                    "RunTests starting. Executing {TestCount} tests",
                    tests.Count());
                var tasks = tests
                    .GroupBy(t => t.Source)
                    .Select(g => (g.Key, g.AsEnumerable()))
                    .Select(async g => (await ((PathEx)g.Key).ReadTestContainerAsync(ct), g.Item2))
                    .Select(async x =>
                    {
                        var (c, tcs) = await x;
                        c.TestExes.ForEach(exe => RunAndRecordTestResultsFromOneExe(
                            exe,
                            tcs,
                            TestRunParams.FromContainer(c),
                            runContext.IsBeingDebugged,
                            frameworkHandle,
                            tl,
                            executorLogger,
                            ct));
                    });

                Task.WaitAll(tasks.ToArray());
            },
            tl.T);
    }

    public override void RunTests(IEnumerable<PathEx> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        var ct = new CancellationToken(_cancelled);
        using var loggingContext =
            new VSTestLoggingContext(frameworkHandle, "Execution");
        var executorLogger =
            loggingContext.CreateLogger(typeof(TestExecutor));
        var commonLogger =
            loggingContext.CreateLogger(typeof(TestDiscovererCommon));
        var tl = new TL { T = _telemetry, L = loggingContext.LegacyLogger, };
        RunWithTelemetry(
            () =>
            {
                executorLogger.LogInformation(
                    new EventId(2, "SourceExecutionStarted"),
                    "RunTests starting. Executing {SourceCount} sources.",
                    sources.Count());
                var tasks = sources.Select(
                    async source => await RunTestsTestsFromOneSourceAsync(
                        await source.ReadTestContainerAsync(ct),
                        runContext,
                        frameworkHandle,
                        tl,
                        executorLogger,
                        commonLogger,
                        ct));
                Task.WaitAll(tasks.ToArray());
            },
            tl.T);
    }

    /// <summary>
    /// Each TestContainer has multiple Exes, Each exe has multiple tests.
    /// Execution of tests happen by running the Exes. All in parallel.
    /// </summary>
    public static async Task RunTestsTestsFromOneSourceAsync(TestContainer container, IRunContext runContext, IFrameworkHandle fh, TL tl, CancellationToken ct)
    {
        var logger = LegacyLoggerBridge.ToMelLogger(tl.L);
        await RunTestsTestsFromOneSourceAsync(
            container,
            runContext,
            fh,
            tl,
            logger,
            logger,
            ct);
    }

    public void Cancel()
    {
        _cancelled = true;
    }

    internal static async Task RunTestsTestsFromOneSourceAsync(
        TestContainer container,
        IRunContext runContext,
        IFrameworkHandle fh,
        TL tl,
        MelLogger logger,
        MelLogger commonLogger,
        CancellationToken ct)
    {
        foreach (var (tsi, tcs) in await container.DiscoverTestCasesFromOneSourceAsync(
            tl,
            commonLogger,
            ct))
        {
            RunAndRecordTestResultsFromOneExe(
                tsi.Exe,
                tcs,
                TestRunParams.FromContainer(container),
                runContext.IsBeingDebugged,
                fh,
                tl,
                logger,
                ct);
        }
    }

    private static void RunAndRecordTestResultsFromOneExe(
        PathEx exe,
        IEnumerable<TestCase> testCases,
        TestRunParams trp,
        bool isBeingDebugged,
        IFrameworkHandle fh,
        TL tl,
        MelLogger logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            new EventId(3, "TestExecutableExecutionStarted"),
            "RunAndRecordTestResultsFromOneExe starting with {Source}, {TestExecutable}, {TestCount}",
            trp.Source,
            exe,
            testCases.Count());
        if (!testCases.Any())
        {
            logger.LogError(
                new EventId(4, "EmptyTestSet"),
                "RunTestsFromOneSourceAsync: Something has gone wrong. Asking to run empty set of test cases. {Source}, {TestExecutable}",
                trp.Source,
                exe);
        }

        try
        {
            var envDict = trp.TestExecutionEnvironment.OverrideProcessEnvironment();
            var testCasesMap = testCases.ToImmutableDictionary(x => x.FullyQualifiedNameRustFormat());
            var args = testCases.Select(tc => tc.FullyQualifiedNameRustFormat());
            var grps = args
                .PartitionBasedOnMaxCombinedLength(20000)
                .Select(x =>
                    x
                        .Concat(new[] { "--exact", "--format", "json", "-Zunstable-options", "--report-time" })
                        .Concat(trp.AdditionalTestExecutionArguments.FromNullSeparatedArray()));
            Parallel.Invoke(
                grps
                    .Select(args => RunTestsFromOneExe(
                        exe,
                        args.ToArray(),
                        testCasesMap,
                        envDict,
                        tl,
                        logger,
                        isBeingDebugged,
                        fh,
                        ct))
                    .Select(t => (Action)(() => t.Wait()))
                    .ToArray());
        }
        catch (Exception e)
        {
            logger.LogError(
                new EventId(5, "TestExecutableExecutionFailed"),
                e,
                "RunTests failed");
            throw;
        }
    }

    private static async Task RunTestsFromOneExe(
        PathEx exe,
        string[] args,
        IReadOnlyDictionary<string, TestCase> testCasesMap,
        IDictionary<string, string> envDict,
        TL tl,
        MelLogger logger,
        bool isBeingDebugged,
        IFrameworkHandle fh,
        CancellationToken ct)
    {
        logger.LogInformation(
            new EventId(6, "TestProcessExecutionStarted"),
            "... RunTestsFromOneExe starting with {TestExecutable}, {ArgumentCount}",
            exe,
            args.Length);
        var trs = Enumerable.Empty<TestResult>();
        if (isBeingDebugged)
        {
            logger.LogInformation(
                new EventId(7, "DebuggerLaunchStarted"),
                "RunTestsFromOneSourceAsync launching test under debugger.");
            var rc = fh.LaunchProcessWithDebuggerAttached(exe, exe.GetDirectoryName(), string.Join(" ", args), envDict);
            if (rc != 0)
            {
                logger.LogError(
                    new EventId(8, "DebuggerLaunchFailed"),
                    "RunTestsFromOneSourceAsync launching test under debugger - returned {ExitCode}.",
                    rc);
            }
        }
        else
        {
            using var testExeProc = await ProcessRunner.RunWithLogging(exe, args, exe.GetDirectoryName(), envDict, ct, tl.L, @throw: false);
            trs = testExeProc.StandardOutputLines
                .Skip(1)
                .Take(testExeProc.StandardOutputLines.Count() - 2)
                .Select(JsonConvert.DeserializeObject<TestRunInfo>)
                .Where(x => x.Event != TestRunInfo.EventType.Started)
                .OrderBy(x => x.FQN)
                .Select(x => ToTestResult(exe, x, testCasesMap));
            var ec = testExeProc.ExitCode ?? 0;
            if (ec != 0 && !trs.Any())
            {
                logger.LogError(
                    new EventId(9, "TestProcessFailed"),
                    "RunTestsFromOneSourceAsync test executable exited with code {ExitCode}.",
                    ec);
                throw new ApplicationException($"Test executable returned {ec}. Check above for the arguments passed to test executable by running it on the command line.");
            }
        }

        foreach (var tr in trs)
        {
            fh.RecordResult(tr);
        }
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

    private static void RunWithTelemetry(Action operation, IFeatureUsageTelemetry telemetry)
    {
        var duration = Stopwatch.StartNew();
        try
        {
            operation();
            telemetry.Track(UsageOperation.TestAdapterExecute, UsageOutcome.Succeeded, duration.Elapsed);
        }
        catch (Exception e)
        {
            var cancelled = e is OperationCanceledException
                || (e is AggregateException aggregate
                    && aggregate.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException));
            telemetry.Track(
                UsageOperation.TestAdapterExecute,
                cancelled ? UsageOutcome.Cancelled : UsageOutcome.Failed,
                duration.Elapsed);
            throw;
        }
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
