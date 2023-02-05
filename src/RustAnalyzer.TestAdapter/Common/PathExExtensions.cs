using System.IO;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class PathExExtensions
{
    public static PathEx Combine(this PathEx path1, PathEx path2)
    {
        return (PathEx)Path.Combine((string)path1, (string)path2);
    }
}
