using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class SpyTestCaseDiscoverySink : ITestCaseDiscoverySink
{
    private readonly List<TestCase> _testCases = new();

    public IReadOnlyList<TestCase> TestCases => _testCases;

    public void SendTestCase(TestCase discoveredTest)
    {
        _testCases.Add(discoveredTest);
    }
}
