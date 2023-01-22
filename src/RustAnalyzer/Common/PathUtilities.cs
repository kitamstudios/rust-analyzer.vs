namespace KS.RustAnalyzer.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

/// <summary>
/// From https://stackoverflow.com/a/74747405/6196679.
/// </summary>
public static class PathUtilities
{
    private static StringComparison StringComparison =>
        IsCaseSensitive ?
            StringComparison.Ordinal :
            StringComparison.OrdinalIgnoreCase;

    private static bool IsCaseSensitive =>
        !(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

    public static string ReplaceInvalidChars(this string filename)
    {
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }

    public static bool ExistsOnPath(this string fileName)
    {
        return SearchInPath(fileName) != null;
    }

    public static string SearchInPath(this string fileName)
    {
        if (File.Exists(fileName))
        {
            return Path.GetFullPath(fileName);
        }

        var values = Environment.GetEnvironmentVariable("PATH");
        foreach (var path in values.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(path, fileName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    public static string MakeRelativePath(string relativeTo, string path)
    {
#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        return Path.GetRelativePath(relativeTo, path);
#else
        return GetRelativePathPolyfill(relativeTo, path);
#endif
    }

#if !(NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER)

    private static string GetRelativePathPolyfill(string relativeTo, string path)
    {
        path = Path.GetFullPath(path);
        relativeTo = Path.GetFullPath(relativeTo);

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        IReadOnlyList<string> p1 = path.Split(separators);
        IReadOnlyList<string> p2 = relativeTo.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var sc = StringComparison;

        int i;
        int n = Math.Min(p1.Count, p2.Count);
        for (i = 0; i < n; i++)
        {
            if (!string.Equals(p1[i], p2[i], sc))
            {
                break;
            }
        }

        if (i == 0)
        {
            // Cannot make a relative path, for example if the path resides on another drive.
            return path;
        }

        p1 = p1.Skip(i).Take(p1.Count - i).ToList();

        if (p1.Count == 1 && p1[0].Length == 0)
        {
            p1 = Array.Empty<string>();
        }

        string relativePath = string.Join(
            new string(Path.DirectorySeparatorChar, 1),
            Enumerable.Repeat("..", p2.Count - i).Concat(p1));

        if (relativePath.Length == 0)
        {
            relativePath = ".";
        }

        return relativePath;
    }
#endif
}
