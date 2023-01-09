using System.Threading.Tasks;

namespace KS.RustAnalyzer.Cargo;

public class CargoManifestFactory
{
    public static Task<CargoManifest> CreateAsync(string cargoFilePath)
    {
        return Task.FromResult(new CargoManifest { Path = cargoFilePath });
    }
}
