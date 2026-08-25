using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

[Trait("type", "UnitTests")]
public class BuildJsonOutputParserTests
{
    [Fact]
    [UseReporter(typeof(RaVsDiffReporter))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void IfNotParsableReturnAsIs()
    {
        var jsonOutput = "   Compiling pest v2.5.2";
        var output = BuildJsonOutputParser.Parse(TestHelpers.ThisTestRoot, jsonOutput, TestHelpers.TL);

        Approvals.VerifyAll(output.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(RaVsDiffReporter))]
    [InlineData("CompilerArtifact1.json")]
    [InlineData("CompilerArtifact2.json")]
    [InlineData("CompilerArtifact3.json")]
    [InlineData("CompilerArtifact4.json")]
    [InlineData("CompilerArtifact5.json")]
    public void ParseCompilerArtifiacts(string dataFile)
    {
        NamerFactory.AdditionalInformation = $"datafile-{dataFile}";
        var jsonOutput = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)dataFile));
        var output = BuildJsonOutputParser.Parse(TestHelpers.ThisTestRoot, jsonOutput, TestHelpers.TL);

        Approvals.VerifyAll(output.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(RaVsDiffReporter))]
    [InlineData("ComplexError1.json")]
    [InlineData("ComplexWarning1.json")]
    [InlineData("ComplexError2.json")]
    [InlineData("ComplexError3.json")]
    [InlineData("ComplexError4.json")]
    public void ParseCompilerMessages(string dataFile)
    {
        NamerFactory.AdditionalInformation = $"datafile-{dataFile}";
        var jsonOutput = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)dataFile));
        var output = BuildJsonOutputParser.Parse((PathEx)@"d:\src\dpt\pls\test_app", jsonOutput, TestHelpers.TL);

        Approvals.VerifyAll(output.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [InlineData("CompilerArtifactTestExecutableDeps.json", true)]
    [InlineData("CompilerArtifactTestExecutableBuildDir.json", true)]
    [InlineData("CompilerArtifactBuildScript.json", false)]
    [InlineData("CompilerArtifactNonTestExecutable.json", false)]
    public void ParseTestExecutable(string dataFile, bool expected)
    {
        var jsonOutput = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)dataFile));

        BuildJsonOutputParser.ParseTestExecutable(jsonOutput).HasValue.Should().Be(expected);
    }

    [Fact]
    public void ParseTestExecutablesIgnoresUnrelatedOutput()
    {
        var firstArtifact = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)"CompilerArtifactTestExecutableDeps.json"));
        var customBuild = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)"CompilerArtifactBuildScript.json"));
        var secondArtifact = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)"CompilerArtifactTestExecutableBuildDir.json"));

        var executables = BuildJsonOutputParser.ParseTestExecutables(
            new[] { "Compiling dependency", firstArtifact, "warning: unrelated", customBuild, "Finished tests", secondArtifact }).ToArray();

        executables.Should().Equal(
            (PathEx)@"D:\src\test\target\release\deps\hello_lib-0123456789abcdef.exe",
            (PathEx)@"D:\src\test\target\release\build\hello_lib\0123456789abcdef\out\int_tests-0123456789abcdef.exe");
    }

    [Fact]
    public void ParseTestExecutablesRejectsMalformedJson()
    {
        var act = () => BuildJsonOutputParser.ParseTestExecutables(new[] { "Compiling dependency", "{not-json" }).ToArray();

        act.Should().Throw<InvalidDataException>()
            .WithMessage("Malformed Cargo JSON protocol record at stdout line 2:*")
            .Where(e => e.InnerException is JsonReaderException);
    }
}
