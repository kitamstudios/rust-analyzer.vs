using System.IO;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class PathExExtensions
{
    public static PathEx Combine(this PathEx path1, PathEx path2) => (PathEx)Path.Combine(path1, path2);

    public static PathEx Combine(this PathEx path1, PathEx path2, PathEx path3) => (PathEx)Path.Combine(path1, path2, path3);

    public static PathEx Combine(this PathEx path1, PathEx path2, PathEx path3, PathEx path4) => (PathEx)Path.Combine(path1, path2, path3, path4);

    public static PathEx GetDirectoryName(this PathEx @this) => (PathEx)Path.GetDirectoryName(@this);
}
