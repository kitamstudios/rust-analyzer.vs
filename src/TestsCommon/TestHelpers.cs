using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Moq;

namespace KS.RustAnalyzer.Tests.Common;

public static class TestHelpers
{
    public static readonly string ThisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    public static readonly PathEx ThisTestRoot2 = (PathEx)ThisTestRoot;

    public static readonly TL TL =
        new ()
        {
            L = Mock.Of<ILogger>(),
            T = Mock.Of<ITelemetryService>(),
        };

    private static readonly ConcurrentDictionary<PathEx, IMetadataService> MetadataServices = new ConcurrentDictionary<PathEx, IMetadataService>();

    // TODO: MS: Remove this.
    public static string RemoveMachineSpecificPaths(this string @this)
        => @this.ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>");

    public static PathEx RemoveMachineSpecificPaths(this PathEx @this)
        => (PathEx)((string)@this).ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>");

    // TODO: MS: test for malformed toml.
    public static IMetadataService MS(this PathEx @this)
    {
        // NOTE: This simulates the case when a folder with multiple workspaces is opened.
        var root = @this.GetDirectoryName();
        return MetadataServices.GetOrAdd(root, (wr) => new MetadataService(new CargoService(TL.T, TL.L), wr, TL));
    }
}
