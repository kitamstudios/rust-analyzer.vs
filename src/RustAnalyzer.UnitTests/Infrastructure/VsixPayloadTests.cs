using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public class VsixPayloadTests
{
    private const string SupportedVisualStudioRange = "[17.12,19.0)";

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
        var vsixes = new[]
        {
            GetCanonicalVsixPath("RustAnalyzer"),
            GetCanonicalVsixPath("RustDevelopmentPack"),
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

    [Fact]
    public void CanonicalRustAnalyzerVsixHasDualHostManifestAndMetadata()
    {
        using (var archive = ZipFile.OpenRead(GetCanonicalVsixPath("RustAnalyzer")))
        {
            var manifestEntries = archive.Entries
                .Where(entry => entry.FullName.Equals("extension.vsixmanifest", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            manifestEntries.Should().ContainSingle();

            using (var stream = manifestEntries[0].Open())
            {
                var manifest = XDocument.Load(stream);
                var ns = manifest.Root.Name.Namespace;
                var metadata = manifest.Root.Element(ns + "Metadata");
                var identities = metadata.Elements(ns + "Identity").ToArray();
                identities.Should().ContainSingle();

                var identity = identities[0];
                identity.Attribute("Id").Value.Should().Be("KS.RustAnalyzer.3a91e56b-fb28-4d85-b572-ec964abf8e31");
                metadata.Element(ns + "Description").Value.Should().Be("Rust language support for Visual Studio 2022 / 2026");

                var generatedMetadata = typeof(RustAnalyzerPackage).Assembly
                    .GetType("KS.RustAnalyzer.Vsix", true)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue());
                generatedMetadata["Id"].Should().Be(identity.Attribute("Id").Value);
                generatedMetadata["Name"].Should().Be(metadata.Element(ns + "DisplayName").Value);
                generatedMetadata["Description"].Should().Be(metadata.Element(ns + "Description").Value);
                generatedMetadata["Language"].Should().Be(identity.Attribute("Language").Value);
                generatedMetadata["Version"].Should().Be(identity.Attribute("Version").Value);
                generatedMetadata["Author"].Should().Be(identity.Attribute("Publisher").Value);
                generatedMetadata["Tags"].Should().Be(metadata.Element(ns + "Tags").Value);

                var installationTargets = manifest.Root
                    .Element(ns + "Installation")
                    .Elements(ns + "InstallationTarget")
                    .ToArray();
                installationTargets.Select(target => target.Attribute("Id").Value).Should().BeEquivalentTo(
                    "Microsoft.VisualStudio.Community",
                    "Microsoft.VisualStudio.Pro",
                    "Microsoft.VisualStudio.Enterprise");
                installationTargets.Should().OnlyContain(
                    target => target.Attribute("Version").Value == SupportedVisualStudioRange);
                installationTargets.Should().OnlyContain(
                    target => target
                        .Elements(ns + "ProductArchitecture")
                        .Select(architecture => architecture.Value)
                        .SequenceEqual(new[] { "amd64" }));

                var coreEditorPrerequisites = manifest.Root
                    .Element(ns + "Prerequisites")
                    .Elements(ns + "Prerequisite")
                    .Where(prerequisite =>
                        prerequisite.Attribute("Id").Value == "Microsoft.VisualStudio.Component.CoreEditor")
                    .ToArray();
                coreEditorPrerequisites.Should().ContainSingle();
                coreEditorPrerequisites[0].Attribute("Version").Value.Should().Be(SupportedVisualStudioRange);
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

    private static string GetCanonicalVsixPath(string projectName)
    {
        var projectsDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        return Path.Combine(projectsDirectory, projectName, $"{projectName}.vsix");
    }
}
