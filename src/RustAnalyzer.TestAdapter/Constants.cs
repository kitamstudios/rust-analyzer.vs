using System;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter;

public static class Constants
{
    public const string ReleaseSummary = $"Bunch of usability fixes for Rust Integration tests experience. Please report any issues you face. Consider giving 5⭐ if you find this extension useful.";
    public const string ReleaseNotesUrl = $"https://github.com/kitamstudios/rust-analyzer.vs/releases/{Vsix.Version}";
    public const string DiscordUrl = "https://discord.gg/JyK55EsACr";
    public const string TestExperienceDemoUrl = "https://youtu.be/pE1Vr2zVCbg?t=170";
    public const string PrerequisitesUrl = "https://github.com/kitamstudios/rust-analyzer.vs/blob/master/PREREQUISITES.md";
    public const string RateExtensionUrl = "https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer&ssr=false#review-details";

    public const string RustLanguageContentType = "rust";
    public const string RustFileExtension = ".rs";
    public const string ManifestFileName = "Cargo.toml"; // NOTE: cargo.exe requires caps 'C'.
    public const string ManifestFileExtension = ".toml";

    public const string RustUpExe = "rustup.exe";
    public const string RAExeNameNoExtension = "rust-analyzer";
    public const string CargoExe = "cargo.exe";

    public const string ConfigurationSectionName = "rust-analyzer.vs";

    public const string ExecutorUriString = "executor://RustTestExecutor/v1";

    public const string ManifestExtension = ".toml";
    public const string TestsContainerExtension = ".rusttests";
    public const string TestContainersSearchPattern = $"*{TestsContainerExtension}";

    public const string RlsLatestInPackageVersion = "2024-07-15";
    public static readonly Version MinimumRequiredVsVersion = new (17, 7);

    public static readonly PathEx TestsContainerExtension2 = (PathEx)TestsContainerExtension;
    public static readonly PathEx ManifestFileName2 = (PathEx)ManifestFileName;
    public static readonly PathEx RustFileExtension2 = (PathEx)".rs";

    public static readonly PathEx CargoExe2 = (PathEx)CargoExe;
}
