using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public sealed class MessageLogger : IMessageLogger
{
    private readonly ITestOutputHelper _output;

    public MessageLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public void SendMessage(TestMessageLevel testMessageLevel, string message)
    {
        _output.WriteLine("{0}: {1}", testMessageLevel, message);
    }
}
