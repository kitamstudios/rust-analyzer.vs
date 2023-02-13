using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.Editor;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Editor;

public class FileContextProviderTests
{
    public static IEnumerable<object[]> GetTestData() => TestData.Get();

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task GetContextsForFileTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        var workspaceRoot = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRootRel);
        var fcp = new FileContextProvider(TestHelpers.MS(workspaceRoot), Mock.Of<ICargoService>(), Mock.Of<IBuildOutputSink>());
        var filePath = workspaceRoot.Combine((PathEx)filePathRel);

        var refInfos = await fcp.GetContextsForFileAsync(filePath, default);
        var processedRefInfos = refInfos.Select(
            ri => new
            {
                ri.ProviderType,
                ri.ContextType,
                Context = new
                {
                    (ri.Context as BuildFileContextBase).BuildConfiguration,
                    WorkspaceRoot = (ri.Context as BuildFileContextBase).BuildTargetInfo.WorkspaceRoot.RemoveMachineSpecificPaths(),
                    (ri.Context as BuildFileContextBase).BuildTargetInfo.Profile,
                    FilePath = (ri.Context as BuildFileContextBase).BuildTargetInfo.FilePath.RemoveMachineSpecificPaths(),
                    (ri.Context as BuildFileContextBase).BuildTargetInfo.AdditionalBuildArgs,
                },
                InputFiles = ri.InputFiles.Select(i => ((PathEx)i).RemoveMachineSpecificPaths()).ToArray(),
                ri.DisplayName,
            });
        Approvals.VerifyAll(processedRefInfos.Select(o => o.SerializeObject(Formatting.Indented, new PathExJsonConverter())), label: string.Empty);
    }
}
