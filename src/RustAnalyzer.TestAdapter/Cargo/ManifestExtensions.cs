using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class ManifestExtensions
{
    public static bool IsManifest(this string @this)
        => Path.GetFileName(@this).Equals(Constants.ManifestFileName, StringComparison.OrdinalIgnoreCase);

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
        var parentManifestPath = await filePath.TryGetParentManifestOrThisUnderWorkspaceAsync(workspaceRoot);
        if (parentManifestPath != null)
        {
            return await Manifest.Create(parentManifestPath, workspaceRoot).ToTask();
        }

        return null;
    }

    public static async Task<string> TryGetParentManifestOrThisUnderWorkspaceAsync(this string fileOrFolderPath, string workspaceRoot)
    {
        if (fileOrFolderPath.IsManifest())
        {
            return await fileOrFolderPath.ToTask();
        }

        var currentPath = fileOrFolderPath;
        while (!currentPath.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase) && (currentPath = Path.GetDirectoryName(currentPath)) != null)
        {
            if (File.Exists(Path.Combine(currentPath, Constants.ManifestFileName)))
            {
                return Path.Combine(currentPath, Constants.ManifestFileName);
            }
        }

        if (currentPath != null && File.Exists(Path.Combine(currentPath, Constants.ManifestFileName)))
        {
            return Path.Combine(currentPath, Constants.ManifestFileName);
        }

        return null;
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