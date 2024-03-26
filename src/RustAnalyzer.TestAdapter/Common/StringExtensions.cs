using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

    public static string ReplaceNullWithBar(this string s)
    {
        return s?.Replace("\0", "|");
    }

    public static string RegexReplace(this string @this, string pattern, string replacement, RegexOptions options = RegexOptions.None)
    {
        return Regex.Replace(@this, pattern, replacement);
    }
}
