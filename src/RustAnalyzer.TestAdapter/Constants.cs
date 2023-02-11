using System;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter;

public static class Constants
{
    public const string ReleaseSummary = $"Significant refactoring of the {ManifestFileName} parser module resulting in simpler code & robust handling of various crates and workspaces scenarios. Please report any issues you face.";
    public const string ReleaseNotesUrl = $"https://github.com/kitamstudios/rust-analyzer.vs/releases/{Vsix.Version}";
    public const string DiscordUrl = "https://discord.gg/JyK55EsACr";

    public const string RustLanguageContentType = "rust";
    public const string RustFileExtension = ".rs";
    public const string ManifestFileName = "Cargo.toml"; // NOTE: cargo.exe requires caps 'C'.
    public const string ManifestFileExtension = ".toml";

    public const string CargoExe = "cargo.exe";

    public const string ConfigurationSectionName = "rust-analyzer";

    public const string ExecutorUriString = "executor://RustTestExecutor/v1";

    public const string ManifestExtension = ".toml";

    public static readonly Version MinimumRequiredVsVersion = new (17, 4);

    public static readonly PathEx ManifestFileName2 = (PathEx)ManifestFileName;
    public static readonly PathEx RustFileExtension2 = (PathEx)".rs";
}
