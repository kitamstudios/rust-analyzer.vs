using System;
using System.IO;
using System.Reflection;

namespace KS.RustAnalyzer.Tests.Common;

public static class TestHelpers
{
    public static readonly string ThisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    public static string RemoveMachineSpecificPaths(this string @this)
        => @this.ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>");
}
