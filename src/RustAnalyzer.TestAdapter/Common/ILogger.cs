namespace KS.RustAnalyzer.TestAdapter.Common;

/// <summary>
/// TODO: Move over to Microsoft.Logging.
/// </summary>
public interface ILogger
{
    void WriteLine(string format, params object[] args);
}
