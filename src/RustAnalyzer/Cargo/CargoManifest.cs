using System;
using System.Collections.Generic;
using System.IO;
using KS.RustAnalyzer.Common;
using Tomlyn;
using Tomlyn.Model;

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

    private readonly TomlTable _model;

    private CargoManifest(string fullPath)
    {
        FullPath = fullPath;
        WorkspaceRoot = Path.GetDirectoryName(fullPath);
        _model = Toml.ToModel(File.ReadAllText(fullPath));
    }

    public string WorkspaceRoot { get; private set; }

    public string FullPath { get; private set; }

    // NOTE: From https://doc.rust-lang.org/cargo/reference/profiles.html#profiles.
    public IEnumerable<string> Profiles => ProfileInfos.Keys;

    public string TargetFileName => $"{TargetFileNameWithoutExtension}{TargetFileExtension}";

    public string TargetFileExtension => GetPackageExtension();

    public string TargetFileNameWithoutExtension => GetPackageName();

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

        throw new NotImplementedException();
    }
}
