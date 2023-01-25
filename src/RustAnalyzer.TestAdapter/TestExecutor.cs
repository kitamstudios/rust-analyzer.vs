using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace KS.RustAnalyzer.TestAdapter;

[ExtensionUri(Constants.ExecutorUriString)]
public class TestExecutor : ITestExecutor
{
    private bool _cancelRequested;

    public void Cancel()
    {
        _cancelRequested = true;
    }

    public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        if (Environment.GetEnvironmentVariable("ATTACH_DEBUGGER_RUSTANALYZER") != null)
        {
            Debugger.Launch();
        }

        foreach (var test in tests)
        {
            if (_cancelRequested)
            {
                break;
            }

            frameworkHandle.RecordResult(new TestResult(test) { Outcome = TestOutcome.Passed });
        }
    }

    public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        if (Environment.GetEnvironmentVariable("ATTACH_DEBUGGER_RUSTANALYZER") != null)
        {
            Debugger.Launch();
        }

        RunTests(sources.Select(s => new TestCase("a.b.c.d", new Uri(Constants.ExecutorUriString), s)), runContext, frameworkHandle);
    }
}
