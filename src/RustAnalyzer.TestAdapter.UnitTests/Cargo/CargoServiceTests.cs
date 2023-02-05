using System.IO;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

// TODO: MS: check for all crate-types in workspace_mixed.
public sealed class CargoServiceTests
{
    [Theory]
    [InlineData(@"workspace_mixed")]
    [UseReporter(typeof(DiffReporter))]
    public async Task GetMetadataTestsAsync(string workspaceRelRoot)
    {
        var workspaceRoot = (PathEx)Path.Combine(TestHelpers.ThisTestRoot, workspaceRelRoot);

        var wmd = await new CargoService(TestHelpers.TL.T, TestHelpers.TL.L).GetMetadata(workspaceRoot, default);

        var normalizedStr = wmd
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(TestHelpers.ThisTestRoot.Replace("\\", "\\\\"), "<TestRoot>");
        Approvals.Verify(normalizedStr);
    }
}
