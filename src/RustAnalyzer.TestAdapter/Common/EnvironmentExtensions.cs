using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class EnvironmentExtensions
{
    public static IDictionary<string, string> OverrideProcessEnvironment(this string @this)
    {
        return @this.ToNullSeparatedDictionary()
            .Concat(GetEnvironmentVariables())
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);
    }

    public static IDictionary<string, string> PrependToPathInEnviroment(this IDictionary<string, string> @this, params PathEx[] directories)
    {
        var pathKey = @this.Keys.First(k => k.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        var dirs = directories.Aggregate(new StringBuilder(), (acc, e) => acc.AppendFormat("{0};", e)).ToString();
        @this[pathKey] = $"{dirs}{@this[pathKey]}";

        return @this;
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
