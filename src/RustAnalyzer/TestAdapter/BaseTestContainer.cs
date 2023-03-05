using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter;

public abstract class BaseTestContainer
{
    public string Source => TestContainerPath;

    public abstract PathEx TestContainerPath { get; }
}
