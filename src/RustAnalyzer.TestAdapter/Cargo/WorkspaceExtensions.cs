using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    private static readonly IReadOnlyDictionary<string, PathEx> ProfileInfos = new Dictionary<string, PathEx>
    {
        ["dev"] = (PathEx)"debug",
        ["release"] = (PathEx)"release",
        ["test"] = (PathEx)"debug",
        ["bench"] = (PathEx)"release",
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
            return @this.Parent.Parent.TargetDirectory.Combine(ProfileInfos[profile], (PathEx)"examples", @this.TargetFileName);
        }
        else
        {
            return @this.Parent.Parent.TargetDirectory.Combine(ProfileInfos[profile], @this.TargetFileName);
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

    public static IEnumerable<string> GetProfiles(this Workspace.Package @this)
    {
        return ProfileInfos.Keys;
    }

    public static bool TryGetParentManifestOrThisUnderWorkspace(this PathEx fileOrFolderPath, PathEx workspaceRoot, out PathEx? parentManifest)
    {
        if (fileOrFolderPath.IsManifest())
        {
            parentManifest = fileOrFolderPath;
            return true;
        }

        if (!fileOrFolderPath.IsContainedIn(workspaceRoot))
        {
            parentManifest = default;
            return false;
        }

        var currentPath = fileOrFolderPath;
        while (currentPath != workspaceRoot)
        {
            currentPath = currentPath.GetDirectoryName();
            if (currentPath.Combine(Constants.ManifestFileName2).FileExists())
            {
                parentManifest = currentPath.Combine(Constants.ManifestFileName2);
                return true;
            }
        }

        if (currentPath.Combine(Constants.ManifestFileName2).FileExists())
        {
            parentManifest = currentPath.Combine(Constants.ManifestFileName2);
            return true;
        }

        parentManifest = null;
        return false;
    }

    public static bool IsManifest(this PathEx @this) => @this.GetFileName() == Constants.ManifestFileName2;

    public static bool IsRustFile(this PathEx @this) => @this.GetExtension() == Constants.RustFileExtension2;

    public static bool IsTestContainer(this PathEx @this) => @this.GetExtension() == Constants.TestsContainerExtension;

    public static async Task<bool> CanHaveExecutableTargetsAsync(this IMetadataService @this, PathEx filePath, CancellationToken ct)
    {
        if (!filePath.IsManifest() && !filePath.IsRustFile())
        {
            return false;
        }

        var p = await @this?.GetContainingPackageAsync(filePath, ct);
        if (p == null)
        {
            return false;
        }

        return p.Targets.Any(t => t.IsRunnable && (t.SourcePath == filePath || t.Parent.ManifestPath == filePath));
    }

    public static bool IsExample(this Workspace.Target @this) => @this.Kinds[0] == Workspace.Kind.Example;
}
