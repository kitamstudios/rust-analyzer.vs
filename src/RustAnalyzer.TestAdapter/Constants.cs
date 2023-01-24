namespace KS.RustAnalyzer.TestAdapter;

public static class Constants
{
    public const string RustLanguageContentType = "rust";
    public const string RustFileExtension = ".rs";
    public const string ManifestFileName = "Cargo.toml"; // NOTE: cargo.exe requires caps 'C'.
    public const string ManifestFileExtension = ".toml";

    public const string CargoExe = "cargo.exe";

    public const string ConfigurationSectionName = "rust-analyzer";

    public const string ExecutorUriString = "executor://RustTestExecutor/v1";

    public const string ManifestExtension = ".toml";
}