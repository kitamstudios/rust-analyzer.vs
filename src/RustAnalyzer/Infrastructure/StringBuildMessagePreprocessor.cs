using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

public sealed class StringBuildMessagePreprocessor
{
    private static readonly Func<PathEx, string, string>[] Processors = new[]
    {
        (PathEx rp, string x) => Regex.Replace(x, @"^(\x1b\[\d*m)+", string.Empty),
        (PathEx rp, string x) => Regex.Replace(x, @"^Diff in \\\\\?\\(.*) at line (\d*)\:", "$1($2,1): warning: diffs created by fmt"),
        (PathEx rp, string x) => Regex.Replace(x, @"^( )*\-\-\> (.*)\:(\d+):(\d+)", $"{rp.Combine((PathEx)"$2")}($3,$4): error: clippy\0$0"),
    };

    public IEnumerable<string> Preprocess(PathEx rootPath, string message)
    {
        return Processors.Aggregate(message, (acc, e) => e(rootPath, acc)).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
