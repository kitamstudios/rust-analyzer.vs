using System;
using System.IO;
using System.Reflection;
using KS.RustAnalyzer.TestAdapter.Common;
using Moq;

namespace KS.RustAnalyzer.Tests.Common;

public static class TestHelpers
{
    public static readonly string ThisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    public static readonly TL TL =
        new ()
        {
            L = Mock.Of<ILogger>(),
            T = Mock.Of<ITelemetryService>(),
        };

    public static string RemoveMachineSpecificPaths(this string @this)
        => @this.ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>");

    public static PathEx RemoveMachineSpecificPaths(this PathEx @this)
        => (PathEx)@this.ToString().Replace(ThisTestRoot.ToUpperInvariant(), "<TestRoot>");
}
