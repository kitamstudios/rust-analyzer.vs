using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class EnvironmentExtensions
{
    private const char EqEscape = '\u0001';
    private static readonly char[] NullSep = new[] { '\0' };
    private static readonly char[] EqSep = new[] { '=' };

    public static string GetEnvironmentValue(this string name) => Environment.GetEnvironmentVariable(name);

    public static IDictionary<string, string> OverrideProcessEnvironment(this string @this)
    {
        // Windows environment variable names are case-insensitive, so an override spelled differently
        // from the process variable has to replace it. Grouping ordinally kept both spellings and let
        // the child process see the value the caller asked to override.
        return @this.ToNullSeparatedDictionary()
            .Concat(GetEnvironmentVariables())
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    public static IDictionary<string, string> PrependToPathInEnviroment(this IDictionary<string, string> @this, params PathEx[] directories)
    {
        var dirs = directories.Aggregate(new StringBuilder(), (acc, e) => acc.AppendFormat("{0};", e)).ToString();
        @this.TryGetValue("PATH", out var path);
        @this["PATH"] = $"{dirs}{path}";

        return @this;
    }

    public static IDictionary<string, string> GetEnvironmentVariables()
    {
        var procEnv = Environment.GetEnvironmentVariables();

        return procEnv.Keys.Cast<string>().ToDictionary(x => x, x => procEnv[x] as string, StringComparer.OrdinalIgnoreCase);
    }

    public static string ToEnvironmentBlock(this IDictionary<string, string> @this)
    {
        return @this
            .Aggregate(new StringBuilder(), (acc, e) => acc.Append($"{e.Key}={e.Value.Replace('=', EqEscape)}\0"))
            .Append('\0')
            .ToString();
    }

    public static IDictionary<string, string> ToNullSeparatedDictionary(this string @this)
    {
        return @this
            .FromNullSeparatedArray()
            .Select(x => x.Split(EqSep, StringSplitOptions.None))
            .ToDictionary(x => x[0], x => x.Length == 2 ? x[1].Replace(EqEscape, '=') : string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public static string[] FromNullSeparatedArray(this string @this)
    {
        return (@this ?? string.Empty).Split(NullSep, StringSplitOptions.RemoveEmptyEntries);
    }
}
