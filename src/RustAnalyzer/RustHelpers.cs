using System;
using System.IO;

namespace KS.RustAnalyzer.VS;

public static class RustHelpers
{
    public static bool IsRustFile(string filename)
    {
        var extension = Path.GetExtension(filename);
        return StringComparer.OrdinalIgnoreCase.Equals(extension, RustConstants.RustFileExtension);
    }

    public static bool IsCargoFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return StringComparer.OrdinalIgnoreCase.Equals(fileName, RustConstants.CargoFileName);
    }

    // TODO: Unit test this.
    public static bool GetParentCargoManifest(string filePath, string projectRoot, out string parentCargoPath)
    {
        var currentPath = filePath;
        while ((currentPath = Path.GetDirectoryName(currentPath)) != null)
        {
            var candidateCargoPath = Path.Combine(currentPath, RustConstants.CargoFileName);
            if (currentPath.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                parentCargoPath = candidateCargoPath;
                return true;
            }

            if (File.Exists(candidateCargoPath))
            {
                parentCargoPath = candidateCargoPath;
                return true;
            }
        }

        parentCargoPath = null;
        return false;
    }
}
