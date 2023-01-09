using System.Collections.Generic;

namespace KS.RustAnalyzer.Cargo;

public class CargoManifest
{
    public CargoManifest(string path)
    {
        Path = path;

        // NOTE: Hard coded for now https://doc.rust-lang.org/cargo/reference/profiles.html#profiles.
        Profiles = new[]
        {
            "dev",
            "release",
            "test",
            "bench",
        };
    }

    public string Path { get; private set; }

    public IEnumerable<string> Profiles { get; private set; }
}
