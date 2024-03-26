using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public static class TestDiscovererCommon
{
    private static readonly Regex TestExecutableFingerPrintCracker = new (@"^(.*)\-[\da-f]{16}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static TL CreateTL(this IMessageLogger @this) => new () { T = new TelemetryService(), L = new TestAdapterLogger(@this) };

    /// <summary>
    /// Each TestContainer contains multiple Exes, each Exes has multiple tests. Each Exe is represented by a TestSuiteInfo.
    /// Enumeration of test cases happen Exe by Exe, in parallel => IAsyncEnumerable[TSI].
    /// </summary>
    public static async IAsyncEnumerable<TestSuiteInfo> FindTestsInSourceAsync(this TestContainer tc, TL tl, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var tsi in new ToolChainService(tl.T, tl.L).GetTestSuiteInfoAsync(tc.ThisPath, tc.Profile, ct))
        {
            yield return tsi;
        }
    }

    /// <summary>
    /// Each Exe is represented by a TestSuiteInfo.
    /// Discovery is done by running Exes in parallel => IAsyncEnumerable[(TestSuiteInfo, IEnumerable[TestCase])].
    /// </summary>
    public static async IAsyncEnumerable<(TestSuiteInfo, IEnumerable<TestCase>)> DiscoverTestCasesFromOneSourceAsync(this TestContainer tc, TL tl, [EnumeratorCancellation] CancellationToken ct)
    {
        tl.L.WriteLine("Starting discovery of tests from {0}.", tc.ThisPath);

        await foreach (var testSuite in tc.FindTestsInSourceAsync(tl, ct))
        {
            var testCaseInfos = testSuite.Tests.Select(t => CreateTestCaseFromTest(testSuite.Container.ThisPath, testSuite.Exe, t));
            tl.T.TrackEvent("DiscoverTestsFromOneSource", ("Source", tc.ThisPath), ("NumberOfTests", $"{testCaseInfos.Count()}"));
            yield return (testSuite, testCaseInfos);
        }
    }

    /// <summary>
    /// Each Exe is represented by a TestSuiteInfo.
    /// Discovery is done by running Exes in parallel => IAsyncEnumerable[(TestSuiteInfo, IEnumerable[TestCase])].
    /// </summary>
    public static async IAsyncEnumerable<(TestSuiteInfo, IEnumerable<TestCase>)> DiscoverTestCasesFromOneExeAsync(this TestSuiteInfo tsi, TL tl, [EnumeratorCancellation] CancellationToken ct)
    {
        tl.L.WriteLine("Starting discovery of tests from {0}.", tsi.Exe);

        await foreach (var testSuite in tsi.Container.FindTestsInSourceAsync(tl, ct))
        {
            var testCaseInfos = testSuite.Tests.Select(t => CreateTestCaseFromTest(testSuite.Container.ThisPath, testSuite.Exe, t));
            tl.T.TrackEvent("DiscoverTestsFromOneSource", ("Source", tsi.Container.ThisPath), ("NumberOfTests", $"{testCaseInfos.Count()}"));
            yield return (testSuite, testCaseInfos);
        }
    }

    public static string RustFQN2TestExplorerFQN(this string rustTestFQN, PathEx exe)
    {
        var strippedExe = (string)exe.GetFileNameWithoutExtension();
        var m = TestExecutableFingerPrintCracker.Match(strippedExe);
        if (m.Success)
        {
            strippedExe = m.Groups[1].Value;
        }

        return $"{strippedExe}.{rustTestFQN.Replace("::", ".")}";
    }

    public static string FullyQualifiedNameRustFormat(this TestCase @this) => @this.FullyQualifiedName.StripNamespace().Replace(".", "::");

    public static string StripNamespace(this string testName) => string.Join(".", testName.Split('.').Skip(1));

    private static TestCase CreateTestCaseFromTest(PathEx testContainer, PathEx testExe, TestSuiteInfo.TestInfo test)
    {
        var fqn = test.FQN.RustFQN2TestExplorerFQN(testExe);
        return new TestCase
        {
            CodeFilePath = test.SourcePath,
            LineNumber = test.StartLine,
            DisplayName = fqn.Split('.').Last(),
            ExecutorUri = new Uri(Constants.ExecutorUriString),
            FullyQualifiedName = fqn,
            Source = testContainer,
        };
    }
}
