using System.Collections.Generic;
using System.IO;
using KS.RustAnalyzer.Common;
using Tomlyn;
using Tomlyn.Model;

namespace KS.RustAnalyzer.Cargo;

/// <summary>
/// NOTE: Assuming defaults to start with. Gradually TDD in the common and then lesser common cases.
/// </summary>
public class Manifest
{
    private static readonly IDictionary<string, string> ProfileInfos = new Dictionary<string, string>
    {
        ["dev"] = "debug",
        ["release"] = "release",
        ["test"] = "debug",
        ["bench"] = "release",
    };

    private readonly TomlTable _model;

    private Manifest(string fullPath)
    {
        FullPath = fullPath;
        _model = Toml.ToModel(File.ReadAllText(fullPath));
    }

    public string WorkspaceRoot => GetWorkspaceRoot(FullPath);

    public string FullPath { get; private set; }

    // NOTE: From https://doc.rust-lang.org/cargo/reference/profiles.html#profiles.
    public IEnumerable<string> Profiles => ProfileInfos.Keys;

    public string TargetFileName => $"{TargetFileNameWithoutExtension}{TargetFileExtension}";

    public string TargetFileExtension => GetPackageExtension();

    public string TargetFileNameWithoutExtension => GetPackageName();

    public string StartupProjectEntryName => $"{Path.GetFileName(Path.GetDirectoryName(FullPath))}";

    public static Manifest Create(string parentCargoPath)
    {
        return new Manifest(parentCargoPath);
    }

    public static bool GetParentCargoManifest(string filePath, string projectRoot, out string parentCargoPath)
    {
        var currentPath = filePath;
        while ((currentPath = Path.GetDirectoryName(currentPath)) != null)
        {
            var candidateCargoPath = Path.Combine(currentPath, Constants.CargoFileName);
            if (File.Exists(candidateCargoPath))
            {
                parentCargoPath = candidateCargoPath;
                return true;
            }
        }

        parentCargoPath = null;
        return false;
    }

    public string GetTargetPathForProfile(string profile)
    {
        return Path.Combine(WorkspaceRoot, "target", ProfileInfos[profile], TargetFileName);
    }

    public string GetTargetPathForProfileRelativeToPath(string profile, string filePath)
    {
        return PathUtilities.MakeRelativePath(Path.GetDirectoryName(filePath), GetTargetPathForProfile(profile));
    }

    private string GetPackageName()
    {
        return ((TomlTable)_model["package"])?["name"]?.ToString();
    }

    private string GetPackageExtension()
    {
        if (File.Exists(Path.Combine(Path.GetDirectoryName(FullPath), @"src\main.rs")))
        {
            return ".exe";
        }
        else if (File.Exists(Path.Combine(Path.GetDirectoryName(FullPath), @"src\lib.rs")))
        {
            return ".rlib";
        }

        return "._ni_";
    }

    private static string GetWorkspaceRoot(string fullPath)
    {
        var currentCargoPath = fullPath;
        while (GetParentCargoManifest(currentCargoPath, Path.GetDirectoryName(currentCargoPath), out string parentCargoPath))
        {
            var model = Toml.ToModel(File.ReadAllText(parentCargoPath));
            if (model.ContainsKey("workspace"))
            {
                return Path.GetDirectoryName(parentCargoPath);
            }

            currentCargoPath = Path.GetDirectoryName(parentCargoPath);
        }

        // NOTE: Current cargo is the workspace.
        return Path.GetDirectoryName(fullPath);
    }
}
