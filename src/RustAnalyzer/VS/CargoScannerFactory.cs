using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.VS;

[ExportFileScanner(
    type: ProviderType,
    language: "rust",
    supportedFileExtensions: new[] { RustConstants.CargoFileName, RustConstants.RustFileExtension, },
    supportedTypes: new[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<FileReferenceInfo>) })]
public class CargoScannerFactory : IWorkspaceProviderFactory<IFileScanner>
{
    public const string ProviderType = "F5628EAD-0000-4683-B597-D8314B971ED6";
    public static readonly Guid ProviderTypeGuid = new (ProviderType);

    public IFileScanner CreateProvider(IWorkspace workspaceContext)
    {
        return new CargoScanner(workspaceContext);
    }
}

public class CargoScanner : IFileScanner
{
    private readonly IWorkspace _workspace;

    public CargoScanner(IWorkspace workspace)
    {
        _workspace = workspace;
    }

    public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken)
        where T : class
    {
        if (typeof(T) == FileScannerTypeConstants.FileDataValuesType)
        {
            IPropertySettings launchSettings = new PropertySettings
            {
                [LaunchConfigurationConstants.NameKey] = $"{"hello_world.exe"} [hello_world {Path.GetFileName(filePath)}]",
                [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                [LaunchConfigurationConstants.ProgramKey] = @"D:\src\delme\hello_world\target\debug\hello_world.exe",
            };

            var ret = new List<FileDataValue>
            {
                new FileDataValue(
                    BuildConfigurationContext.ContextTypeGuid,
                    BuildConfigurationContext.DataValueName,
                    value: null,
                    target: null,
                    context: "Debugxxx"),

                new FileDataValue(
                    DebugLaunchActionContext.ContextTypeGuid,
                    DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                    launchSettings,
                    target: @"D:\src\delme\hello_world\target\debug\hello_world.exe")
            };

            return await Task.FromResult((T)(IReadOnlyCollection<FileDataValue>)ret);
        }
        else if (typeof(T) == FileScannerTypeConstants.FileReferenceInfoType)
        {
            var ret = new List<FileReferenceInfo>
            {
                new FileReferenceInfo(@"D:\src\delme\hello_world\target\debug\hello_world.exe", null, "Debugxxx", (int)FileReferenceInfoType.Output)
            };

            return await Task.FromResult((T)(IReadOnlyCollection<FileReferenceInfo>)ret);
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}
