using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.Common;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Cargo;

public class BuildJsonOutputParserTests
{
    private static readonly ILogger L = Mock.Of<ILogger>();
    private static readonly ITelemetryService T = Mock.Of<ITelemetryService>();
    private static readonly string ThisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    [Fact]
    [UseReporter(typeof(DiffReporter))]
    public void IfNotParsableReturnAsIs()
    {
        var jsonOutput = "   Compiling pest v2.5.2";
        var output = RustAnalyzer.Cargo.BuildJsonOutputParser.Parse(ThisTestRoot, jsonOutput, L, T);

        Approvals.VerifyAll(output.Select(SerializeObject), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [InlineData("CompilerArtifact1.json")]
    [InlineData("CompilerArtifact2.json")]
    [InlineData("CompilerArtifact3.json")]
    public void ParseCompilerArtifiacts(string dataFile)
    {
        NamerFactory.AdditionalInformation = $"datafile-{dataFile}";
        var jsonOutput = File.ReadAllText(Path.Combine(ThisTestRoot, dataFile));
        var output = RustAnalyzer.Cargo.BuildJsonOutputParser.Parse(ThisTestRoot, jsonOutput, L, T);

        Approvals.VerifyAll(output.Select(SerializeObject), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [InlineData("ComplexError1.json")]
    [InlineData("ComplexWarning1.json")]
    [InlineData("ComplexError2.json")]
    [InlineData("ComplexError3.json")]
    [InlineData("ComplexError4.json")]
    public void ParseCompilerMessages(string dataFile)
    {
        NamerFactory.AdditionalInformation = $"datafile-{dataFile}";
        var jsonOutput = File.ReadAllText(Path.Combine(ThisTestRoot, dataFile));
        var output = RustAnalyzer.Cargo.BuildJsonOutputParser.Parse(ThisTestRoot, jsonOutput, L, T);

        Approvals.VerifyAll(output.Select(SerializeObject), label: string.Empty);
    }

    public static string SerializeObject(object obj)
    {
        var str = JsonConvert.SerializeObject(obj, Formatting.Indented);
        return Regex.Replace(str, @"\r\n?|\n", "\n");
    }
}
