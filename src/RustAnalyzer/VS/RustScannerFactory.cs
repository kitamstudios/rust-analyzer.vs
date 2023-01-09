using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.VS;

[ExportFileScanner(
    type: ProviderType,
    language: "Rust",
    supportedFileExtensions: new[] { RustConstants.CargoFileName, RustConstants.RustFileExtension, },
    supportedTypes: new[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<FileReferenceInfo>) })]
public class RustScannerFactory : IWorkspaceProviderFactory<IFileScanner>
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
        var parentCargoManifest = await _workspace.GetParentCargoManifestAsync(filePath);
        if (parentCargoManifest == null)
        {
            return (T)(IReadOnlyCollection<T>)Array.Empty<T>();
        }

        if (typeof(T) == FileScannerTypeConstants.FileDataValuesType)
        {
            var ret = GetFileDataValues(parentCargoManifest, filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileDataValue>)ret);
        }
        else if (typeof(T) == FileScannerTypeConstants.FileReferenceInfoType)
        {
            var ret = GetFileReferenceInfos(parentCargoManifest);
            return await Task.FromResult((T)(IReadOnlyCollection<FileReferenceInfo>)ret);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    private List<FileDataValue> GetFileDataValues(Cargo.CargoManifest parentCargoManifest, string filePath)
    {
        var allFileDataValues = new List<FileDataValue>();

        if (RustHelpers.IsCargoFile(filePath))
        {
            IPropertySettings launchSettings = new PropertySettings
            {
                [LaunchConfigurationConstants.NameKey] = $"{"hello_world.exe"} [{_workspace.MakeRelative(filePath)}]",
                [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
            };

            allFileDataValues.Add(
                new FileDataValue(
                    DebugLaunchActionContext.ContextTypeGuid,
                    DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                    launchSettings,
                    target: null,
                    context: null));
        }

        var fileDataValuesForAllProfiles = parentCargoManifest.Profiles.SelectMany(
            profile => new[]
                {
                    new FileDataValue(
                        BuildConfigurationContext.ContextTypeGuid,
                        BuildConfigurationContext.DataValueName,
                        value: null,
                        target: null,
                        context: profile),

                    new FileDataValue(
                        BuildConfigurationContext.ContextTypeGuid,
                        BuildConfigurationContext.DataValueName,
                        value: null,
                        target: $@"D:\src\ks\rust-analyzer\src\TestProjects\hello_world\target\{profile}\hello_world.exe",
                        context: profile),
                });

        allFileDataValues.AddRange(fileDataValuesForAllProfiles);

        return allFileDataValues;
    }

    private static List<FileReferenceInfo> GetFileReferenceInfos(Cargo.CargoManifest parentCargoManifest)
    {
        return parentCargoManifest.Profiles
            .Select(
                profile => new FileReferenceInfo($@"target\{profile}\hello_world.exe", $@"D:\src\ks\rust-analyzer\src\TestProjects\hello_world\target\{profile}\hello_world.exe", profile, (int)FileReferenceInfoType.Output))
            .ToList();
    }
}
