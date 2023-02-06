using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class ManifestExtensions
{
    public static bool IsManifest(this PathEx @this) => ((string)@this).IsManifest();

    public static bool IsManifest(this string @this)
        => Path.GetFileName(@this).Equals(Constants.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    public static bool IsRustFile(this PathEx @this) => ((string)@this).IsRustFile();

    public static bool IsRustFile(this string @this)
        => Path.GetExtension(@this).Equals(Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> CanHaveExecutableTargetsAsync(this string @this, string workspaceRoot)
    {
        if (!(@this.IsNotNullOrEmpty() && File.Exists(@this) && (@this.IsManifest() || @this.IsRustFile())))
        {
            return await false.ToTask();
        }

        var manifest = await @this.GetParentManifestOrThisUnderWorkspaceAsync(workspaceRoot);
        return manifest != null && (await manifest.GetTargets()).Where(t => t.IsRunnable && t.Source.Equals(@this, StringComparison.OrdinalIgnoreCase)).Any();
    }

    public static async Task<Manifest> GetParentManifestOrThisUnderWorkspaceAsync(this string filePath, string workspaceRoot)
    {
        var found = ((PathEx)filePath).TryGetParentManifestOrThisUnderWorkspace((PathEx)workspaceRoot, out PathEx? parentManifestPath);
        if (found)
        {
            return await Manifest.Create(parentManifestPath, workspaceRoot).ToTask();
        }

        return null;
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

    public static Task<IEnumerable<Target>> GetTargets(this Manifest @this)
    {
        if (@this == null)
        {
            return Enumerable.Empty<Target>().ToTask();
        }

        return @this.Targets.ToTask();
    }
}