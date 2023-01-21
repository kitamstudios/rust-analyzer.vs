namespace KS.RustAnalyzer.Common;

public interface IOutputWindowPane
{
    void Clear();

    void Initialize();

    void WriteLine(string message);
}
