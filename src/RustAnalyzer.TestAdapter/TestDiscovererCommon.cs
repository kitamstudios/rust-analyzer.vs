using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public static class TestDiscovererCommon
{
    public static TL CreateTL(this IMessageLogger @this) => new () { T = new TelemetryService(), L = new TestAdapterLogger(@this) };

    public static Task<TestSuiteInfo> FindTestsInSourceAsync(this TestContainer tc, TL tl, CancellationToken ct)
    {
        return new ToolChainService(tl.T, tl.L).GetTestSuiteInfoAsync(tc.ThisPath, tc.Profile, ct);
    }

    public static async Task<IEnumerable<TestCase>> DiscoverTestCasesFromOneSourceAsync(this TestContainer tc, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("Starting discovery of tests from {0}.", tc.ThisPath);

        var testSuite = await tc.FindTestsInSourceAsync(tl, ct);
        var testCaseInfos = testSuite.Tests.Select(t => CreateTestCaseFromTest(testSuite.Container, t));
        tl.T.TrackEvent("DiscoverTestsFromOneSource", ("Source", tc.ThisPath), ("NumberOfTests", $"{testCaseInfos.Count()}"));

        return testCaseInfos;
    }

    public static string RustTestFQN2TestExplorerFQN(this string rustTestFQN) => rustTestFQN.Replace("::", ".");

    public static string TestExplorerFQN2RustTestFQN(this string rustTestFQN) => rustTestFQN.Replace(".", "::");

    private static TestCase CreateTestCaseFromTest(PathEx testContainer, TestSuiteInfo.TestInfo test)
    {
        var fqn = test.FQN.RustTestFQN2TestExplorerFQN();
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
