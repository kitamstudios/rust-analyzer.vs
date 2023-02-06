using System.Linq;
using System.Threading.Tasks;
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

// TODO: MS: check for all crate-types in workspace_mixed.
public sealed class CargoServiceTests
{
    [Theory]
    [InlineData(@"hello_workspace")]
    [InlineData(@"workspace_mixed")]
    [UseReporter(typeof(DiffReporter))]
    public async Task GetMetadataTestsAsync(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot2.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await new CargoService(TestHelpers.TL.T, TestHelpers.TL.L).GetWorkspaceAsync(manifestPath, default);

        var normalizedStr = wmd
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(TestHelpers.ThisTestRoot.Replace("\\", "\\\\"), "<TestRoot>");
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"hello_workspace")]
    [InlineData(@"workspace_mixed")]
    [UseReporter(typeof(DiffReporter))]
    public async Task CheckParentsTestsAsync(string workspaceRelRoot)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var manifestPath = TestHelpers.ThisTestRoot2.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var wmd = await new CargoService(TestHelpers.TL.T, TestHelpers.TL.L).GetWorkspaceAsync(manifestPath, default);
        var targetParents = wmd.Packages.Select(p => (p, tp: p.Targets.Select(t => t.Parent)));

        wmd.Packages.Should().OnlyContain(p => p.Parent == wmd);
        targetParents.Should().OnlyContain(e => e.tp.All(p => p == e.p));
    }
}
