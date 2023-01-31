using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using KS.RustAnalyzer.VS;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.VS;

public class FileScannerTests
{
    public static IEnumerable<object[]> GetTestData() => TestData.Get();

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task ScanContentFileRefInfoTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        string workspaceRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootRel);
        var fs = new FileScanner(workspaceRoot);
        var filePath = Path.Combine(workspaceRoot, filePathRel);

        var refInfos = await fs.ScanContentAsync<IReadOnlyCollection<FileReferenceInfo>>(filePath, default);
        var processedRefInfos = refInfos.Select(
            ri => new
            {
                WorkspacePath = ri.WorkspacePath.ToLowerInvariant(),
                Target = ri.Target.RemoveMachineSpecificPaths(),
                ri.Context,
                ri.ReferenceType,
            });
        Approvals.VerifyAll(processedRefInfos.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task ScanContentFileDataValueTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        string workspaceRoot = Path.Combine(TestHelpers.ThisTestRoot, workspaceRootRel);
        var fs = new FileScanner(workspaceRoot);
        var filePath = Path.Combine(workspaceRoot, filePathRel);

        var dataValues = await fs.ScanContentAsync<IReadOnlyCollection<FileDataValue>>(filePath, default);
        var processedDataValues = dataValues.Select(
            dv => new
            {
                dv.Type,
                dv.Name,
                Value = NormalizeAndSerializeDataValue(dv.Value),
                Target = dv.Target?.RemoveMachineSpecificPaths(),
                dv.Context,
            });
        Approvals.VerifyAll(processedDataValues.Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    private static object NormalizeAndSerializeDataValue(object value)
    {
        if (value is PropertySettings ps)
        {
            ps[LaunchConfigurationConstants.ProjectKey] =
                ps[LaunchConfigurationConstants.ProjectKey].ToString().RemoveMachineSpecificPaths();
            ps[LaunchConfigurationConstants.ProgramKey] =
                ps[LaunchConfigurationConstants.ProgramKey].ToString().RemoveMachineSpecificPaths();
            return ps
                .Aggregate(new StringBuilder("{ "), (acc, e) => acc.AppendFormat("[{0}] = {1}, ", e.Key, e.Value))
                .Append(" }")
                .ToString();
        }

        return value;
    }
}
