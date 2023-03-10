using System;
using System.Collections.Generic;
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

    public static Uri ToUri(this string s)
    {
        return new Uri(s);
    }

    public static bool IsNullOrEmpty(this string s)
    {
        return string.IsNullOrEmpty(s);
    }

    public static bool IsNullOrEmptyOrWhiteSpace(this string s)
    {
        return string.IsNullOrWhiteSpace(s);
    }

    public static bool IsNotNullOrEmpty(this string s)
    {
        return !s.IsNullOrEmpty();
    }

    public static string[] GetSpaceSeperatedParts(this string @this) => (@this ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    public static IDictionary<string, string> GetDictionaryFromEnvironmentBlock(this string @this)
    {
        var nullSep = new[] { '\0' };
        var entrySep = new[] { "=" };
        return (@this ?? string.Empty)
            .Split(nullSep, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split(entrySep, StringSplitOptions.RemoveEmptyEntries))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0], x => x[1]);
    }
}
