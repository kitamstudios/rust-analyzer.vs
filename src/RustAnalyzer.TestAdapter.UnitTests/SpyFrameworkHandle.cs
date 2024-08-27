using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class SpyFrameworkHandle : IFrameworkHandle
{
    private readonly ConcurrentBag<TestResult> _results = new();
    private readonly ITestOutputHelper _output;

    public SpyFrameworkHandle(ITestOutputHelper output)
    {
        _output = output;
    }

    public IReadOnlyCollection<TestResult> Results => _results;

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
        _results.Add(testResult);
    }

    public void RecordStart(TestCase testCase)
    {
        throw new NotImplementedException();
    }

    public void SendMessage(TestMessageLevel testMessageLevel, string message)
    {
        _output.WriteLine("{0}: {1}", testMessageLevel, message);
    }
}
