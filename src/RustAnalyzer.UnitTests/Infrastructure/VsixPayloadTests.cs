using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public class VsixPayloadTests
{
    private static readonly string[] HostAssemblyPrefixes =
    {
        "EnvDTE",
        "Microsoft.ServiceHub.",
        "Microsoft.TestPlatform.",
        "Microsoft.VisualStudio.",
        "VSLangProj",
    };

    private static readonly string[] HostAssemblyNames =
    {
        "stdole.dll",
        "StreamJsonRpc.dll",
        "System.ComponentModel.Composition.dll",
    };

    [Fact]
    public void CanonicalVsixesExcludeHostAssemblies()
    {
        var projectsDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        var vsixes = new[]
        {
            Path.Combine(projectsDirectory, "RustAnalyzer", "RustAnalyzer.vsix"),
            Path.Combine(projectsDirectory, "RustDevelopmentPack", "RustDevelopmentPack.vsix"),
        };

        foreach (var vsix in vsixes)
        {
            using (var archive = ZipFile.OpenRead(vsix))
            {
                var hostAssemblies = FindHostAssemblies(archive);

                hostAssemblies.Should().BeEmpty($"{Path.GetFileName(vsix)} must use host-provided assemblies");
            }
        }
    }

    [Theory]
    [InlineData("EnvDTE.dll")]
    [InlineData("envdte80.DLL")]
    [InlineData("stdole.dll")]
    [InlineData("VSLangProj.dll")]
    [InlineData("vslangproj165.DLL")]
    [InlineData("StreamJsonRpc.dll")]
    [InlineData("system.componentmodel.composition.DLL")]
    [InlineData("Microsoft.ServiceHub.Framework.dll")]
    [InlineData("Microsoft.TestPlatform.ObjectModel.dll")]
    [InlineData("Microsoft.VisualStudio.LanguageServer.Client.dll")]
    [InlineData("Microsoft.VisualStudio.TestWindow.Interfaces.dll")]
    [InlineData("Microsoft.VisualStudio.Workspace.dll")]
    public void SyntheticVsixDetectsHostAssembly(string assemblyName)
    {
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                archive.CreateEntry($"Contents/{assemblyName}");
            }

            stream.Position = 0;
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                FindHostAssemblies(archive).Should().Equal(assemblyName);
            }
        }
    }

    [Fact]
    public void SyntheticVsixAllowsExtensionOwnedAssemblies()
    {
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                archive.CreateEntry("Contents/KS.RustAnalyzer.dll");
                archive.CreateEntry("Contents/Microsoft.ApplicationInsights.dll");
                archive.CreateEntry("Contents/Community.VisualStudio.Toolkit.dll");
            }

            stream.Position = 0;
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                FindHostAssemblies(archive).Should().BeEmpty();
            }
        }
    }

    private static string[] FindHostAssemblies(ZipArchive archive)
    {
        return archive.Entries
            .Select(entry => Path.GetFileName(entry.FullName))
            .Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(name =>
                HostAssemblyNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                HostAssemblyPrefixes.Any(prefix =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}
