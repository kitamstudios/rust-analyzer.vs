using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class StringExtensions
{
    private static readonly char[] NullSep = new[] { '\0' };
    private static readonly char[] EqSep = new[] { '=' };

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

    public static string ReplaceNullWithBar(this string s)
    {
        return s?.Replace("\0", "|");
    }

    public static IDictionary<string, string> ToNullSeparatedDictionary(this string @this)
    {
        return @this
            .ToNullSeparatedArray()
            .Select(x => x.Split(EqSep, StringSplitOptions.None))
            .ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : string.Empty);
    }

    public static string[] ToNullSeparatedArray(this string @this)
    {
        return (@this ?? string.Empty).Split(NullSep, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string RegexReplace(this string @this, string pattern, string replacement)
    {
        return Regex.Replace(@this, pattern, replacement);
    }
}
