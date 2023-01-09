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

    public static string GetParentCargoManifest(string filePath, string projectRoot)
    {
        var currentPath = Path.GetDirectoryName(filePath);

        while (true)
        {
            var candidateCargoPath = Path.Combine(currentPath, "cargo.toml");
            if (currentPath.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return candidateCargoPath;
            }

            if (File.Exists(candidateCargoPath))
            {
                return candidateCargoPath;
            }

            currentPath = Path.GetDirectoryName(currentPath);
        }
    }
}
