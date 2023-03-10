using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class ToolChainServiceExtensions
{
    public static bool IsTestDiscoveryComplete(this TestContainer @this)
    {
        return @this.TestExe != TestContainer.NotYetGeneratedMarker && @this.TestExe.FileExists();
    }

    public static async Task<TestContainer> ReadTestContainerAsync(this PathEx @this, CancellationToken ct)
    {
        return JsonConvert.DeserializeObject<TestContainer>(await @this.ReadAllTextAsync(ct));
    }

    public static Task WriteTestContainerAsync(this PathEx @this, PathEx manifestPath, PathEx targetPath, PathEx sourcePath, string profile, PathEx? testExePath, CancellationToken ct)
    {
        return @this.WriteAllTextAsync(
            JsonConvert.SerializeObject(
                new TestContainer
                {
                    ThisPath = @this,
                    Manifest = manifestPath,
                    Source = sourcePath,
                    TargetDir = targetPath,
                    Profile = profile,
                    TestExe = testExePath ?? TestContainer.NotYetGeneratedMarker,
                },
                new PathExJsonConverter()),
            ct);
    }
}
