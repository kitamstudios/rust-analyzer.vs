using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;
using Tomlyn;
using Tomlyn.Model;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

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

    public string FullPath { get; }

    /// <summary>
    /// From https://doc.rust-lang.org/cargo/reference/profiles.html#profiles.
    /// </summary>
    public IEnumerable<string> Profiles => ProfileInfos.Keys;

    public bool IsPackage => _model.ContainsKey(KeyNamePackage);

    public bool IsWorkspace => _model.ContainsKey(KeyNameWorkspace);

    /// <summary>
    /// Gets the targets from this manifest. Each manifest can have one or more targets.
    /// Ref: https://doc.rust-lang.org/cargo/reference/cargo-targets.html.
    /// </summary>
    public IEnumerable<Target> Targets => GetTargets();

    public IEnumerable<Target> GetTargets()
    {
        return new[]
        {
            new Target(this),
        };
    }

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
        var parentManifestPath = Path.Combine(workspaceRoot, Constants.ManifestFileName);
        var currentPath = filePath;
        while ((currentPath = Path.GetDirectoryName(currentPath)) != null)
        {
            var candidateManifestPath = Path.Combine(currentPath, Constants.ManifestFileName);
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
        return StringComparer.OrdinalIgnoreCase.Equals(fileName, Constants.ManifestFileName);
    }

    public string GetTargetPathForProfile(string profile)
    {
        return Path.Combine(WorkspaceRoot, FolderNameTarget, ProfileInfos[profile], Targets.ToArray()[0].TargetFileName);
    }

    public string GetTargetPathForProfileRelativeToPath(string profile, string filePath)
    {
        return PathUtilities.MakeRelativePath(Path.GetDirectoryName(filePath), GetTargetPathForProfile(profile));
    }

    public string GetPackageName()
    {
        Ensure.That(IsPackage, nameof(IsPackage)).IsTrue();

        return ((TomlTable)_model[KeyNamePackage])?[ValueNameName]?.ToString() ?? "<[package] section must have a name>";
    }

    // TODO: Why is this recursing over folders like its only caller?
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
