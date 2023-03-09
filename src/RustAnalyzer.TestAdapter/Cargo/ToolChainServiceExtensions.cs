using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class ToolChainServiceExtensions
{
    public static bool IsTestDiscoveryComplete(this TestContainer @this)
    {
        return @this.TestExe != TestContainer.NotYetGeneratedMarker && @this.TestExe.FileExists();
    }
}
