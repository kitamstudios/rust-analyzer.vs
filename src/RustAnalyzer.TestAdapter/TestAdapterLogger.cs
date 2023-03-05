using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public class TestAdapterLogger : ILogger
{
    private readonly IMessageLogger _logger;

    public TestAdapterLogger(IMessageLogger logger)
    {
        _logger = logger;
    }

    public void WriteError(string format, params object[] args)
    {
        _logger.SendMessage(TestMessageLevel.Error, "ra.vs> " + string.Format(format, args));
    }

    public void WriteLine(string format, params object[] args)
    {
        _logger.SendMessage(TestMessageLevel.Informational, "ra.vs> " + string.Format(format, args));
    }
}