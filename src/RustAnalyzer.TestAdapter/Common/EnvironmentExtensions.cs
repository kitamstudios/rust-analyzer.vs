using System;
using System.Collections.Generic;
using System.Linq;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class EnvironmentExtensions
{
    // TODO: RELEASE: Test for all test settings variables.
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
