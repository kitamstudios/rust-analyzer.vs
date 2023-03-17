using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class EnvironmentExtensions
{
    // TODO: RELEASE: Test for all test settings variables.
    public static IDictionary<string, string> ToDictionary(this string @this)
    {
        var nullSep = new[] { '\0' };
        var entrySep = new[] { "=" };
        return (@this ?? string.Empty)
            .Split(nullSep, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split(entrySep, StringSplitOptions.None))
            .ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : string.Empty);
    }

    public static IDictionary<string, string> OverrideProcessEnvironment(this string @this)
    {
        return @this.ToDictionary()
            .Concat(GetEnvironmentVariables())
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);
    }

    public static IDictionary<string, string> GetEnvironmentVariables()
    {
        var procEnv = Environment.GetEnvironmentVariables();

        return procEnv.Keys.Cast<string>().ToDictionary(x => x, x => procEnv[x] as string);
    }

    public static string ToEnvironmentBlock(this IDictionary<string, string> @this)
    {
        return @this
            .Aggregate(new StringBuilder(), (acc, e) => acc.Append($"{e.Key}={e.Value}\0"))
            .Append('\0')
            .ToString();
    }
}
