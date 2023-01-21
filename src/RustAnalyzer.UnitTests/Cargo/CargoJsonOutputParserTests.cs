using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using KS.RustAnalyzer.Cargo;
using KS.RustAnalyzer.Common;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Cargo;

public class CargoJsonOutputParserTests
{
    private static readonly ILogger _l = Mock.Of<ILogger>();
    private static readonly ITelemetryService _t = Mock.Of<ITelemetryService>();
    private static readonly string _thisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData");

    [Fact]
    public void IfNotParsableIgnore()
    {
        var jsonOutput = "   Compiling pest v2.5.2";
        var output = BuildJsonOutputParser.Parse(jsonOutput, _l, _t);

        output.Should().BeEmpty();
    }

    [Theory]
    [InlineData(
        "CompilerArtifact1.json",
        new[] { @"   Compiling pls_core_extras v0.1.0 (D:\src\dpt\pls\pls_core_extras)" })]
    [InlineData(
        "CompilerArtifact2.json",
        new[] { "   Compiling tera v1.17.1" })]
    [InlineData(
        "CompilerArtifact3.json",
        new string[0])]
    public void ParseCompilerArtifiacts(string dataFile, string[] expected)
    {
        var jsonOutput = File.ReadAllText(Path.Combine(_thisTestRoot, dataFile));
        var output = BuildJsonOutputParser.Parse(jsonOutput, _l, _t);

        output.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(
        "ComplexError1.json",
        @"D:\src\dpt\pls\test_app\src\main.rs(26,23): error RS0000: expected one of `!`, `,`, `.`, `::`, `?`, `{`, `}`, or an operator, found `redConsoleLogger`")]
    [InlineData(
        "ComplexWarning1.json",
        @"D:\src\dpt\pls\test_app\src\main.rs(2,5): warning RS0000: unused import: `pls_core_extras::logger::ColoredConsoleLogger`")]
    [InlineData(
        "ComplexError2.json",
        @"D:\src\dpt\pls\test_app\src\main.rs: error RS0000: aborting due to previous error; 1 warning emitted")]
    public void ParseCompilerMessages(string dataFile, string expected)
    {
        var jsonOutput = File.ReadAllText(Path.Combine(_thisTestRoot, dataFile));
        var output = BuildJsonOutputParser.Parse(jsonOutput, _l, _t);

        output[0].Should().Be(expected);
        output[1].Should().NotBeNullOrWhiteSpace();
    }
}
