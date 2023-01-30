using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

// TODO: MS: This enum needs to go.
public enum TargetType
{
    Bin,
    Lib,
    Example,
    Test,
    Bench,
}

[DebuggerDisplay("{QualifiedTargetFileName}")]
public class Target
{
    /// <summary>
    /// NOTE:
    /// - Extension: Could be .rlib, .dll, .lib as per.https://doc.rust-lang.org/reference/linkage.html. Going with the default for now.
    /// </summary>
    public static readonly IReadOnlyDictionary<TargetType, (string SectionName, string Prefix, string Extension)> TargetTypeInfos =
        new Dictionary<TargetType, (string, string, string)>
        {
            [TargetType.Bin] = ("bin", string.Empty, ".exe"),
            [TargetType.Lib] = ("lib", "lib", ".rlib"),
            [TargetType.Example] = ("example", string.Empty, ".exe"),
            [TargetType.Test] = ("test", string.Empty, ".exe"),
            [TargetType.Bench] = ("bench", string.Empty, ".exe"),
        };

    public Target(Manifest manifest, string name, TargetType type)
    {
        Manifest = manifest;
        Name = name;
        Type = type;
        Source = Manifest.FullPath;
        AdditionalBuildArgs = string.Empty;
    }

    public Manifest Manifest { get; }

    public string Name { get; }

    public TargetType Type { get; }

    public virtual string Source { get; protected set; }

    public bool IsRunnable => Type == TargetType.Bin || Type == TargetType.Example;

    public string QualifiedTargetFileName => $"[{Type.ToString().ToLowerInvariant()}: {GetTargetPathRelativeToWorkspace()}] {TargetFileName}";

    public string TargetFileName => $"{TargetTypeInfos[Type].Prefix}{Name}{TargetTypeInfos[Type].Extension}";

    public virtual string AdditionalBuildArgs { get; protected set; }

    public virtual string GetPath(string profile) => Path.Combine(GetTargetDirectory(profile), TargetFileName);

    public string GetPathRelativeTo(string profile, string rootPath)
        => PathUtilities.MakeRelativePath(Path.GetDirectoryName(rootPath), GetPath(profile));

    // TODO: MS: This needs to be from cargo metadata output.
    protected string GetTargetDirectory(string profile)
        => Path.Combine(Manifest.WorkspaceRoot, "target", Manifest.ProfileInfos[profile]);

    private string GetTargetPathRelativeToWorkspace()
    {
        var relPath = Path.GetDirectoryName(PathUtilities.MakeRelativePath(Manifest.WorkspaceRoot, Manifest.FullPath));
        return @$"{relPath}\";
    }
}
