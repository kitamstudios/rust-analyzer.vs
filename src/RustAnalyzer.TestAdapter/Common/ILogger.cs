namespace KS.RustAnalyzer.TestAdapter.Common;

public interface ILogger
{
    void WriteLine(string format, params object[] args);

    void WriteError(string format, params object[] args);
}
