using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using ApprovalTests.Reporters.TestFrameworks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public class BuildJsonOutputParserTests
{
    [Fact]
    [UseReporter(typeof(XUnit2Reporter))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void IfNotParsableReturnAsIs()
    {
        var jsonOutput = "   Compiling pest v2.5.2";
        var output = BuildJsonOutputParser.Parse(TestHelpers.ThisTestRoot, jsonOutput, TestHelpers.TL);

        Approvals.VerifyAll(output.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(XUnit2Reporter))]
    [InlineData("CompilerArtifact1.json")]
    [InlineData("CompilerArtifact2.json")]
    [InlineData("CompilerArtifact3.json")]
    public void ParseCompilerArtifiacts(string dataFile)
    {
        NamerFactory.AdditionalInformation = $"datafile-{dataFile}";
        var jsonOutput = File.ReadAllText(TestHelpers.ThisTestRoot.Combine((PathEx)dataFile));
        var output = BuildJsonOutputParser.Parse(TestHelpers.ThisTestRoot, jsonOutput, TestHelpers.TL);

        Approvals.VerifyAll(output.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(XUnit2Reporter))]
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
}
