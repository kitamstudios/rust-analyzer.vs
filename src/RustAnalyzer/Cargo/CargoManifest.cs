using System.Collections.Generic;
using System.IO;
using KS.RustAnalyzer.Common;

namespace KS.RustAnalyzer.Cargo;

/// <summary>
/// NOTE: Assuming defaults to start with. Gradually TDD in the common and then lesser common cases.
/// </summary>
public class CargoManifest
{
    private static readonly IDictionary<string, string> ProfileInfos = new Dictionary<string, string>
    {
        ["dev"] = "debug",
        ["release"] = "release",
        ["test"] = "debug",
        ["bench"] = "release",
    };

    private CargoManifest(string fullPath)
    {
        FullPath = fullPath;
        WorkspaceRoot = Path.GetDirectoryName(fullPath);
    }

    public string WorkspaceRoot { get; private set; }

    public string FullPath { get; private set; }

    // NOTE: From https://doc.rust-lang.org/cargo/reference/profiles.html#profiles.
    public IEnumerable<string> Profiles => ProfileInfos.Keys;

    public string TargetFileName => $"{TargetFileNameWithoutExtension}{TargetFileExtension}";

    public string TargetFileExtension => ".exe";

    public string TargetFileNameWithoutExtension => Path.GetFileNameWithoutExtension(FullPath);

    public string StartupProjectEntryName => $"{TargetFileName} [{PathUtilities.MakeRelativePath(WorkspaceRoot, FullPath)}]";

    public static CargoManifest Create(string parentCargoPath)
    {
        return new CargoManifest(parentCargoPath);
    }

    public string GetTargetPathForProfile(string profile)
    {
        return $@"{Path.GetDirectoryName(FullPath)}\target\{ProfileInfos[profile]}\{TargetFileName}";
    }

    public string GetTargetPathForProfileRelativeToPath(string profile, string filePath)
    {
        return PathUtilities.MakeRelativePath(Path.GetDirectoryName(filePath), GetTargetPathForProfile(profile));
    }
}
