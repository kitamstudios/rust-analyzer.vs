using System.IO;
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
}
