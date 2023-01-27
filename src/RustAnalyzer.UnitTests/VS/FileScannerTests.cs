using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.VS;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.VS;

public class FileScannerTests
{
    private static readonly string ThisTestRoot =
        Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    public static IEnumerable<object[]> GetTestData()
    {
        return new[]
        {
            new[] { @"hello_library", @"Cargo.toml" },
            new[] { @"hello_library", @"src\lib.rs" },
            new[] { @"hello_world", @"Cargo.toml" },
            new[] { @"hello_world", @"src\main.rs" },
            new[] { @"hello_workspace", @"Cargo.toml" },
            new[] { @"hello_workspace", @"main\Cargo.toml" },
            new[] { @"hello_workspace", @"main\src\main.rs" },
            new[] { @"hello_workspace", @"shared\Cargo.toml" },
            new[] { @"hello_workspace", @"shared\src\lib.rs" },
            new[] { @"hello_workspace2", @"Cargo.toml" },
            new[] { @"workspace_mixed", @"Cargo.toml" },
            new[] { @"workspace_mixed", @"src\libx.rs" },
            new[] { @"workspace_mixed", @"src\main.rs" },
            new[] { @"workspace_mixed", @"shared\Cargo.toml" },
            new[] { @"workspace_mixed", @"shared\src\lib.rs" },
        }.AsEnumerable();
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task ScanContentFileRefInfoTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        string workspaceRoot = Path.Combine(ThisTestRoot, workspaceRootRel);
        var fs = new FileScanner(workspaceRoot);
        var filePath = Path.Combine(workspaceRoot, filePathRel);

        var refInfos = await fs.ScanContentAsync<IReadOnlyCollection<FileReferenceInfo>>(filePath, default);
        var processedRefInfos = refInfos.Select(
            ri => new
            {
                WorkspacePath = ri.WorkspacePath.ToLowerInvariant(),
                Target = ri.Target.ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>"),
                ri.Context,
                ri.ReferenceType,
            });
        Approvals.VerifyAll(processedRefInfos, label: string.Empty);
    }

    [Theory]
    [UseReporter(typeof(DiffReporter))]
    [MemberData(nameof(GetTestData))]
    public async Task ScanContentFileDataValueTestsAsync(string workspaceRootRel, string filePathRel)
    {
        NamerFactory.AdditionalInformation = $"{Path.Combine(workspaceRootRel, filePathRel).ReplaceInvalidChars()}";
        string workspaceRoot = Path.Combine(ThisTestRoot, workspaceRootRel);
        var fs = new FileScanner(workspaceRoot);
        var filePath = Path.Combine(workspaceRoot, filePathRel);

        var dataValues = await fs.ScanContentAsync<IReadOnlyCollection<FileDataValue>>(filePath, default);
        var processedDataValues = dataValues.Select(
            dv => new
            {
                dv.Type,
                dv.Name,
                Value = SerializeDataValue(dv.Value),
                Target = dv.Target?.ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>"),
                dv.Context,
            });
        Approvals.VerifyAll(processedDataValues, label: string.Empty);
    }

    private static object SerializeDataValue(object value)
    {
        if (value is PropertySettings propSettings)
        {
            return propSettings
                .Aggregate(new StringBuilder("{ "), (acc, e) => acc.AppendFormat("[{0}] = {1}, ", e.Key, e.Value))
                .Append(" }")
                .ToString();
        }

        return value;
    }
}
