using System;
using System.IO;
using System.Linq;
using System.Text;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class StringExtensions
{
    public static Stream ToStreamUTF8(this string s)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(s));
    }

    public static bool IsNullOrEmpty(this string s)
    {
        return string.IsNullOrEmpty(s);
    }

    public static bool IsNotNullOrEmpty(this string s)
    {
        return !s.IsNullOrEmpty();
    }

    // Environment variables should be passed as a null-terminated block of null-terminated strings. Each string is in the following form:name=value\0.
    public static string GetEnvironmentBlock(this string @this)
    {
        var entrySep = new[] { " " };
        var kvSep = new[] { "=" };
        return @this
            .Split(entrySep, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split(kvSep, StringSplitOptions.RemoveEmptyEntries))
            .Where(s => s.Length == 2)
            .Aggregate(new StringBuilder(), (acc, e) => acc.AppendFormat("{0}={1}\0", e[0], e[1]))
            .Append('\0')
            .ToString();
    }
}
