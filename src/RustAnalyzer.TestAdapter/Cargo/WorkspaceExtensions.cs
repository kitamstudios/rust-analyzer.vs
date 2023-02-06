using System.Collections.Generic;
using System.IO;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class WorkspaceExtensions
{
    public static readonly IReadOnlyDictionary<Workspace.CrateType, (string Prefix, string Extension)> CrateTypeInfos =
        new Dictionary<Workspace.CrateType, (string, string)>
        {
            [Workspace.CrateType.Lib] = ("lib", ".rlib"),
            [Workspace.CrateType.RLib] = ("lib", ".rlib"),
            [Workspace.CrateType.DyLib] = (string.Empty, ".dylib"),
            [Workspace.CrateType.CdyLib] = (string.Empty, ".cydlib"),
            [Workspace.CrateType.StaticLib] = (string.Empty, ".staticlib"),
            [Workspace.CrateType.ProcMacro] = (string.Empty, ".procmacro"),
            [Workspace.CrateType.Bin] = (string.Empty, ".exe"),
        };

    public static IEnumerable<Workspace.Target> GetTargets(this Workspace.Package @this) => @this.Targets;

    public static PathEx CreateTargetFileName(this Workspace.Target @this)
    {
        return (PathEx)$"{CrateTypeInfos[@this.CrateTypes[0]].Prefix}{@this.Name}{CrateTypeInfos[@this.CrateTypes[0]].Extension}";
    }

    public static PathEx GetPath(this Workspace.Target @this, string profile)
    {
        if (@this.Kinds[0] == Workspace.Kind.Example)
        {
            return @this.Parent.Parent.TargetDirectory.Combine((PathEx)Manifest.ProfileInfos[profile], (PathEx)"examples", @this.TargetFileName);
        }
        else
        {
            return @this.Parent.Parent.TargetDirectory.Combine((PathEx)Manifest.ProfileInfos[profile], @this.TargetFileName);
        }
    }

    public static PathEx GetPathRelativeTo(this Workspace.Target @this, string profile, string rootPath)
    {
        return (PathEx)PathExtensions.MakeRelativePath(Path.GetDirectoryName(rootPath), @this.GetPath(profile));
    }

    public static PathEx GetTargetPathRelativeToWorkspace(this Workspace.Target @this)
    {
        var relPath = Path.GetDirectoryName(PathExtensions.MakeRelativePath(@this.Parent.WorkspaceRoot, @this.Parent.FullPath));
        return (PathEx)@$"{relPath}\";
    }
}
