using System;
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
using Microsoft.VisualStudio.Workspace.Indexing;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Editor;

public class FileScannerTests
{
    public static IEnumerable<object[]> GetTestData() => TestData.Get();

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task ScanContentFileRefInfoTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        var workspaceRoot = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRootRel);
        var fs = new FileScanner(TestHelpers.MS(workspaceRoot));
        var filePath = workspaceRoot.Combine((PathEx)filePathRel);

        var refInfos = await fs.ScanContentAsync<IReadOnlyCollection<FileReferenceInfo>>(filePath, default);
        var processedRefInfos = refInfos.Select(
            ri => new
            {
                WorkspacePath = ri.WorkspacePath.ToLowerInvariant(),
                Target = (PathEx)ri.Target,
                ri.Context,
                ri.ReferenceType,
            });
        Approvals.VerifyAll(
            processedRefInfos.Select(
                o => o
                    .SerializeObject(Formatting.Indented, new PathExJsonConverter())
                    .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase)),
            label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task ScanContentFileDataValueTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        var workspaceRoot = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRootRel);
        var fs = new FileScanner(TestHelpers.MS(workspaceRoot));
        var filePath = workspaceRoot.Combine((PathEx)filePathRel);

        var dataValues = await fs.ScanContentAsync<IReadOnlyCollection<FileDataValue>>(filePath, default);
        var processedDataValues = dataValues.Select(
            dv => new
            {
                dv.Type,
                dv.Name,
                dv.Value,
                dv.Target,
                dv.Context,
            });
        Approvals.VerifyAll(
            processedDataValues.Select(
                o => o
                    .SerializeObject(Formatting.Indented, new PathExJsonConverter())
                    .Replace(((string)TestHelpers.ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase)),
            label: string.Empty);
    }
}
