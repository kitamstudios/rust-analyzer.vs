using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class PathExExtensions
{
    public static readonly string[] LineSeperators = new[] { "\r", "\n", "\r\n" };

    public static bool FileExists(this PathEx @this) => File.Exists(@this);

    public static void FileDelete(this PathEx @this) => File.Delete(@this);

    public static bool DirectoryExists(this PathEx @this) => Directory.Exists(@this);

    public static PathEx GetExtension(this PathEx @this) => (PathEx)Path.GetExtension(@this);

    public static PathEx GetFileName(this PathEx @this) => (PathEx)Path.GetFileName(@this);

    public static PathEx GetFullPath(this PathEx @this) => (PathEx)Path.GetFullPath(@this);

    public static PathEx Combine(this PathEx path1, PathEx path2) => (PathEx)Path.Combine(path1, path2);

    public static PathEx Combine(this PathEx path1, PathEx path2, PathEx path3) => (PathEx)Path.Combine(path1, path2, path3);

    public static PathEx Combine(this PathEx path1, PathEx path2, PathEx path3, PathEx path4) => (PathEx)Path.Combine(path1, path2, path3, path4);

    public static PathEx GetDirectoryName(this PathEx @this) => (PathEx)Path.GetDirectoryName(@this);

    public static PathEx GetFileNameWithoutExtension(this PathEx @this) => (PathEx)Path.GetFileNameWithoutExtension(@this);

    public static PathEx GetTempFileName() => (PathEx)Path.GetTempFileName();

    public static PathEx MakeRelativePath(this PathEx relativeTo, PathEx path) => (PathEx)((string)relativeTo).MakeRelativePath(path);

    public static bool IsContainedIn(this PathEx @this, PathEx potentialParent)
    {
        var pp = ((string)potentialParent).TrimEnd(Path.DirectorySeparatorChar);

        return ((string)@this).StartsWith(pp, PathEx.DefaultComparison);
    }

    public static async Task WriteAllTextAsync(this PathEx @this, string content, CancellationToken ct)
    {
        using var fs = new FileStream(@this, FileMode.Create, FileAccess.ReadWrite);
        using var file = new StreamWriter(fs);
        await file.WriteAsync(content);
    }

    public static async Task<string> ReadAllTextAsync(this PathEx @this, CancellationToken ct)
    {
        using var fs = new FileStream(@this, FileMode.Open, FileAccess.Read);
        using var file = new StreamReader(fs);
        return await file.ReadToEndAsync();
    }
}
