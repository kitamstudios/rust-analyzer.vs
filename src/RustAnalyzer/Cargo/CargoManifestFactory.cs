using System.Threading.Tasks;

namespace KS.RustAnalyzer.Cargo;

public class CargoManifestFactory
{
    public static Task<CargoManifest> CreateAsync(string configFile)
    {
        return Task.FromResult(new CargoManifest());
    }
}
