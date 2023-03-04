using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class SpyFrameworkHandle : IFrameworkHandle
{
    private readonly List<string> _results = new ();

    public IReadOnlyList<string> Results => _results;

    public bool EnableShutdownAfterTestRun { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public int LaunchProcessWithDebuggerAttached(string filePath, string workingDirectory, string arguments, IDictionary<string, string> environmentVariables)
    {
        throw new NotImplementedException();
    }

    public void RecordAttachments(IList<AttachmentSet> attachmentSets)
    {
        throw new NotImplementedException();
    }

    public void RecordEnd(TestCase testCase, TestOutcome outcome)
    {
        throw new NotImplementedException();
    }

    public void RecordResult(TestResult testResult)
    {
        _results.Add($"{testResult.TestCase.FullyQualifiedName} - {testResult.Outcome}");
    }

    public void RecordStart(TestCase testCase)
    {
        throw new NotImplementedException();
    }

    public void SendMessage(TestMessageLevel testMessageLevel, string message)
    {
    }
}