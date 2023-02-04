using System;
using System.IO;
using System.Linq;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class ManifestExtensions
{
    public static bool IsManifest(this string @this)
        => Path.GetFileName(@this).Equals(Constants.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    public static bool IsRustFile(this string @this)
        => Path.GetExtension(@this).Equals(Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase);

    public static bool CanHaveExecutableTargets(this string @this, string workspaceRoot)
    {
        if (!(@this.IsNotNullOrEmpty() && File.Exists(@this) && (@this.IsManifest() || @this.IsRustFile())))
        {
            return false;
        }

        var manifest = @this.GetParentManifestOrThisUnderWorkspace(workspaceRoot);
        return manifest != null && manifest.Targets.Where(t => t.IsRunnable && t.Source.Equals(@this, StringComparison.OrdinalIgnoreCase)).Any();
    }

    public static Manifest GetParentManifestOrThisUnderWorkspace(this string filePath, string workspaceRoot)
    {
        if (filePath.TryGetParentManifestOrThisUnderWorkspace(workspaceRoot, out string parentManifestPath))
        {
            return Manifest.Create(parentManifestPath);
        }

        return null;
    }

    public static bool TryGetParentManifestOrThisUnderWorkspace(this string fileOrFolderPath, string workspaceRoot, out string parentCargoPath)
    {
        if (fileOrFolderPath.IsManifest())
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

        if (currentPath != null && File.Exists(Path.Combine(currentPath, Constants.ManifestFileName)))
        {
            parentCargoPath = Path.Combine(currentPath, Constants.ManifestFileName);
            return true;
        }

        parentCargoPath = null;
        return false;
    }
}