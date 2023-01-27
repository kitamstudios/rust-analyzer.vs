using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AutoMapper;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public enum TargetType
{
    Bin,
    Lib,
    Example,
    Test,
    Bench,
}

[DebuggerDisplay("{Name}, {Type}, {TargetFileName}")]
public class Target
{
    /// <summary>
    /// NOTE:
    /// - Extension: Could be .rlib, .dll, .lib as per.https://doc.rust-lang.org/reference/linkage.html. Going with the simplest for now.
    /// </summary>
    public static readonly IReadOnlyDictionary<TargetType, (string SectionName, string Extension)> TargetTypeInfos =
        new Dictionary<TargetType, (string SectionName, string Extension)>
        {
            [TargetType.Bin] = ("bin", ".exe"),
            [TargetType.Lib] = ("lib", ".lib"),
            [TargetType.Example] = ("example", ".exe"),
            [TargetType.Test] = ("test", ".exe"),
            [TargetType.Bench] = ("bench", ".exe"),
        };

    public Target(Manifest manifest, string name, TargetType type)
    {
        Manifest = manifest;
        Name = name;
        Type = type;
    }

    public Manifest Manifest { get; }

    public string Name { get; }

    public TargetType Type { get; }

    public bool IsRunnable => Type != TargetType.Lib;

    public string QualifiedTargetFileName => $"[{Type.ToString().ToLowerInvariant()}: {GetTargetPathRelativeToWorkspace()}] {TargetFileName}";

    public string TargetFileName => $"{Name}{TargetTypeInfos[Type].Extension}";

    public string GetPath(string profile)
    {
        return Path.Combine(Manifest.WorkspaceRoot, Manifest.FolderNameTarget, Manifest.ProfileInfos[profile], TargetFileName);
    }

    public string GetPathRelativeTo(string profile, string rootPath)
    {
        return PathUtilities.MakeRelativePath(Path.GetDirectoryName(rootPath), GetPath(profile));
    }

    private string GetTargetPathRelativeToWorkspace()
    {
        var relPath = Path.GetDirectoryName(PathUtilities.MakeRelativePath(Manifest.WorkspaceRoot, Manifest.FullPath));
        return @$"{relPath}\";
    }
}