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

    public static Task<TestSuiteInfo> FindTestsInSourceAsync(this PathEx source, TL tl, CancellationToken ct)
    {
        return new ToolChainService(tl.T, tl.L).GetTestSuiteInfoAsync(source, Constants.DefaultTestProfile, ct);
    }

    public static async Task<IEnumerable<TestCase>> DiscoverTestCasesFromOneSourceAsync(this PathEx source, TL tl, CancellationToken ct)
    {
        tl.L.WriteLine("Starting discovery of tests from {0}.", source);

        var testSuite = await source.FindTestsInSourceAsync(tl, ct);
        var testCaseInfos = testSuite.Tests.Select(t => CreateTestCaseFromTest(testSuite.Container, t));
        tl.T.TrackEvent("DiscoverTestsFromOneSource", ("Source", source), ("NumberOfTests", $"{testCaseInfos.Count()}"));

        return testCaseInfos;
    }

    private static TestCase CreateTestCaseFromTest(PathEx container, TestSuiteInfo.TestInfo test)
    {
        var fqnParts = test.FQN.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        return new TestCase
        {
            CodeFilePath = test.SourcePath,
            LineNumber = test.StartLine,
            DisplayName = fqnParts[fqnParts.Length - 1],
            ExecutorUri = new Uri(Constants.ExecutorUriString),
            FullyQualifiedName = string.Join(".", fqnParts),
            Source = container,
        };
    }
}
