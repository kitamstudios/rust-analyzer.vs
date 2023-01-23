using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EnsureThat;
using KS.RustAnalyzer.Common;
using Tomlyn;
using Tomlyn.Model;

namespace KS.RustAnalyzer.Cargo;

/// <summary>
/// NOTE: Assuming defaults to start with. Gradually TDD in the common and then lesser common cases.
/// </summary>
[DebuggerDisplay("{FullPath}, IsWorkspace = {IsWorkspace}, IsPackage = {IsPackage}")]
public class Manifest
{
    public const string KeyNamePackage = "package";
    public const string KeyNameWorkspace = "workspace";
    public const string FolderNameTarget = "target";
    public const string ValueNameName = "name";

    public static readonly IReadOnlyDictionary<string, string> ProfileInfos = new Dictionary<string, string>
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

    public bool IsPackage => _model.ContainsKey(KeyNamePackage);

    public bool IsWorkspace => _model.ContainsKey(KeyNameWorkspace);

    public bool IsLibrary => File.Exists(Path.Combine(Path.GetDirectoryName(FullPath), @"src\lib.rs"));

    public bool IsBinary => File.Exists(Path.Combine(Path.GetDirectoryName(FullPath), @"src\main.rs"));

    public bool Is(string filePath)
    {
        return FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase);
    }

    public static Manifest Create(string parentCargoPath)
    {
        return new Manifest(parentCargoPath);
    }

    public static Manifest GetParentManifest(string workspaceRoot, string filePath)
    {
        if (TryGetParentManifest(workspaceRoot, filePath, out string parentManifestPath))
        {
            return Create(parentManifestPath);
        }

        return null;
    }

    public static bool TryGetParentManifest(string workspaceRoot, string filePath, out string parentCargoPath)
    {
        var parentManifestPath = Path.Combine(workspaceRoot, Constants.CargoFileName);
        var currentPath = filePath;
        while ((currentPath = Path.GetDirectoryName(currentPath)) != null)
        {
            var candidateManifestPath = Path.Combine(currentPath, Constants.CargoFileName);
            if (File.Exists(candidateManifestPath) && candidateManifestPath.Equals(parentManifestPath, StringComparison.OrdinalIgnoreCase))
            {
                parentCargoPath = candidateManifestPath;
                return true;
            }
        }

        parentCargoPath = null;
        return false;
    }

    public static bool IsManifest(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return StringComparer.OrdinalIgnoreCase.Equals(fileName, Constants.CargoFileName);
    }

    public string GetTargetPathForProfile(string profile)
    {
        return Path.Combine(WorkspaceRoot, FolderNameTarget, ProfileInfos[profile], TargetFileName);
    }

    public string GetTargetPathForProfileRelativeToPath(string profile, string filePath)
    {
        return PathUtilities.MakeRelativePath(Path.GetDirectoryName(filePath), GetTargetPathForProfile(profile));
    }

    private string GetPackageName()
    {
        Ensure.That(IsPackage, nameof(IsPackage)).IsTrue();

        return ((TomlTable)_model[KeyNamePackage])?[ValueNameName]?.ToString() ?? "<[package] section must have a name>";
    }

    private string GetPackageExtension()
    {
        Ensure.That(IsPackage, nameof(IsPackage)).IsTrue();

        if (IsBinary)
        {
            return ".exe";
        }
        else if (IsLibrary)
        {
            return ".rlib";
        }

        return "._ni_";
    }

    private static string GetWorkspaceRoot(string fullPath)
    {
        var currentCargoPath = fullPath;
        while (TryGetParentManifest(Path.GetDirectoryName(currentCargoPath), currentCargoPath, out string parentCargoPath))
        {
            var model = Toml.ToModel(File.ReadAllText(parentCargoPath));
            if (model.ContainsKey(KeyNameWorkspace))
            {
                return Path.GetDirectoryName(parentCargoPath);
            }

            currentCargoPath = Path.GetDirectoryName(parentCargoPath);
        }

        // NOTE: Current cargo is the workspace.
        return Path.GetDirectoryName(fullPath);
    }
}
