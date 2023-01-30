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
    /// Gets the default profiles for now.
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

    public bool Is(string filePath) => FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase);

    public static Manifest Create(string parentCargoPath)
    {
        try
        {
            return new (parentCargoPath);
        }
        catch
        {
            // NOTE: In case the toml is malformed.
            return null;
        }
    }

    public static Manifest GetParentManifestOrThisUnderWorkspace(string workspaceRoot, string filePath)
    {
        if (TryGetParentManifestOrThisUnderWorkspace(workspaceRoot, filePath, out string parentManifestPath))
        {
            return Create(parentManifestPath);
        }

        return null;
    }

    public static bool TryGetParentManifestOrThisUnderWorkspace(string workspaceRoot, string fileOrFolderPath, out string parentCargoPath)
    {
        if (Path.GetFileName(fileOrFolderPath).Equals(Constants.ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            parentCargoPath = fileOrFolderPath;
            return true;
        }

        var currentPath = fileOrFolderPath;
        while (!currentPath.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase) && (currentPath = Path.GetDirectoryName(currentPath)) != null)
        {
            if (File.Exists(Path.Combine(currentPath, Constants.ManifestFileName)))
            {
                parentCargoPath = Path.Combine(currentPath, Constants.ManifestFileName);
                return true;
            }
        }

        if (File.Exists(Path.Combine(currentPath, Constants.ManifestFileName)))
        {
            parentCargoPath = Path.Combine(currentPath, Constants.ManifestFileName);
            return true;
        }

        parentCargoPath = null;
        return false;
    }

    public static bool IsManifest(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return StringComparer.OrdinalIgnoreCase.Equals(fileName, Constants.ManifestFileName);
    }

    public string GetPackageName()
    {
        Ensure.That(IsPackage, nameof(IsPackage)).IsTrue();

        return ((TomlTable)_model[KeyNamePackage])?[ValueNameName]?.ToString() ?? "package_section_must_have_a_name";
    }

    private string GetDefaultTargetName() => GetPackageName().Replace("-", "_");

    /// <summary>
    /// TODO: MS: Use this eventually cargo metadata --no-deps --format-version 1 --manifest-path D:\src\ks\rust-analyzer\src\TestProjects\hello_workspace\subfolder\shared2\Cargo.toml | ConvertFrom-Json.
    /// </summary>
    private static string GetWorkspaceRoot(string fullPath)
    {
        var currentPath = fullPath;
        while (TryGetParentManifestOrThisUnderWorkspace(Path.GetDirectoryName(Path.GetDirectoryName(currentPath)), currentPath, out string parentCargoPath))
        {
            var model = Toml.ToModel(File.ReadAllText(parentCargoPath));
            if (model.ContainsKey(KeyNameWorkspace))
            {
                return Path.GetDirectoryName(parentCargoPath);
            }

            currentPath = Path.GetDirectoryName(parentCargoPath);
        }

        // NOTE: Current cargo is the workspace.
        return Path.GetDirectoryName(fullPath);
    }

    /// <summary>
    /// TODO: MS: This should move to Target class.
    /// </summary>
    private IEnumerable<Target> GetTargets()
    {
        if (!IsPackage)
        {
            return Enumerable.Empty<Target>();
        }

        var definedTargets = EnumExtensions
            .GetEnumValues<TargetType>()
            .Where(t => t != TargetType.Example)
            .SelectMany(GetTargetsForOneType)
            .Concat(ExampleTarget.GetAll(this));

        if (definedTargets.Any())
        {
            return definedTargets;
        }

        return GetAutoDiscoveredTargets();
    }

    /// <summary>
    /// NOTE: Only the very basic auto-discovery https://doc.rust-lang.org/cargo/reference/cargo-targets.html#target-auto-discovery.
    /// </summary>
    private IEnumerable<Target> GetAutoDiscoveredTargets()
    {
        var autoDiscoveredTargets = new List<Target>();
        if (File.Exists(Path.Combine(Path.GetDirectoryName(FullPath), @"src\lib.rs")))
        {
            autoDiscoveredTargets.Add(new Target(this, GetDefaultTargetName(), TargetType.Lib));
        }

        if (File.Exists(Path.Combine(Path.GetDirectoryName(FullPath), @"src\main.rs")))
        {
            autoDiscoveredTargets.Add(new Target(this, GetDefaultTargetName(), TargetType.Bin));
        }

        // NOTE: Here we neither have any explicitly define targets, neither do we have any lib.rs or main.rs.
        if (!autoDiscoveredTargets.Any())
        {
            autoDiscoveredTargets.Add(new Target(this, GetDefaultTargetName(), TargetType.Bin));
        }

        return autoDiscoveredTargets;
    }

    private IEnumerable<Target> GetTargetsForOneType(TargetType type)
    {
        string targetDefSectionName = Target.TargetTypeInfos[type].SectionName;
        if (!_model.ContainsKey(targetDefSectionName))
        {
            yield break;
        }

        object targetDefSection = _model[targetDefSectionName];
        var targetDefTables = targetDefSection is TomlTable table
            ? new[] { table }
            : targetDefSection is TomlTableArray tableArray
              ? tableArray.ToArray()
              : throw new InvalidOperationException(string.Format($"{targetDefSectionName} is neither a TomlTable nor a TomlTableArray."));

        foreach (var targetDefTable in targetDefTables)
        {
            var targetName = targetDefTable.TryGetValue(ValueNameName, out var name)
                ? name.ToString()
                : GetDefaultTargetName();
            yield return new Target(this, targetName, type);
        }
    }
}
