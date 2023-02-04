using System;
using System.IO;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public static class ManifestExtensions
{
    public static bool IsManifest(this string @this)
        => Path.GetFileName(@this).Equals(Constants.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    public static bool IsRustFile(this string @this)
        => Path.GetExtension(@this).Equals(Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase);

    // TODO: unit test this.
    public static bool IsRustExample(this string @this)
    {
        if (!@this.IsRustFile())
        {
            return false;
        }

        var parentDirName = Path.GetFileName(Path.GetDirectoryName(@this));
        if ("examples".Equals(parentDirName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parentParentDirName = Path.GetFileName(Path.GetDirectoryName(parentDirName));
        if ("examples".Equals(parentParentDirName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}