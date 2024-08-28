using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public static class TestDiscovererCommon
{
    private static readonly Regex TestExecutableFingerPrintCracker = new(@"^(.*)\-[\da-f]{16}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static TL CreateTL(this IMessageLogger @this) => new() { T = new TelemetryService(), L = new TestAdapterLogger(@this) };

    /// <summary>
    /// Each TestContainer contains multiple Exes, each Exes has multiple tests. Each Exe is represented by a TestSuiteInfo.
    /// Enumeration of test cases happen Exe by Exe, in parallel => IAsyncEnumerable[TSI].
    /// </summary>
    public static async Task<IEnumerable<Task<TestSuiteInfo>>> FindTestsInSourceAsync(this TestContainer tc, TL tl, CancellationToken ct)
    {
        return await new ToolchainService(tl.T, tl.L).GetTestSuiteInfoAsync(tc.ThisPath, tc.Profile, ct);
    }

    /// <summary>
    /// Each Exe is represented by a TestSuiteInfo.
    /// Discovery is done by running Exes in parallel => IAsyncEnumerable[(TestSuiteInfo, IEnumerable[TestCase])].
    /// </summary>
    public static async Task<IEnumerable<(TestSuiteInfo TSI, IEnumerable<TestCase> TCs)>> DiscoverTestCasesFromOneSourceAsync(this TestContainer tc, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("Starting discovery of tests from {0}.", tc.ThisPath);

        var ret = new List<(TestSuiteInfo, IEnumerable<TestCase>)>();
        foreach (var suite in await tc.FindTestsInSourceAsync(tl, ct))
        {
            var tsi = await suite;
            var testCaseInfos = tsi.Tests.Select(t => CreateTestCaseFromTest(tsi.Container.ThisPath, tsi.Exe, t));
            tl.T.TrackEvent("DiscoverTestsFromOneSource", ("Source", tc.ThisPath), ("NumberOfTests", $"{testCaseInfos.Count()}"));
            ret.Add((tsi, testCaseInfos));
        }

        return ret;
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
