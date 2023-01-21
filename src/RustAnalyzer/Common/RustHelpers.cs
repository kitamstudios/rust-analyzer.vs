using System;
using System.IO;

namespace KS.RustAnalyzer.Common;

public static class RustHelpers
{
    public static bool IsRustFile(string filename)
    {
        var extension = Path.GetExtension(filename);
        return StringComparer.OrdinalIgnoreCase.Equals(extension, Constants.RustFileExtension);
    }

    public static bool IsCargoFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return StringComparer.OrdinalIgnoreCase.Equals(fileName, Constants.CargoFileName);
    }
}
