using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public abstract class TestsWithLogger
{
    private readonly ITestOutputHelper _output;

    protected TestsWithLogger(ITestOutputHelper output)
    {
        _output = output;
        FrameworkHandle = new SpyFrameworkHandle(_output);
    }

    protected SpyFrameworkHandle FrameworkHandle { get; private set; }

    protected IMessageLogger MessageLogger => new MessageLogger(_output);
}
