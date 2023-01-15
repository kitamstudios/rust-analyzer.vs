using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Cargo;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.VS;

[ExportFileScanner(
    type: ProviderType,
    language: "Rust",
    supportedFileExtensions: new[] { RustConstants.CargoFileName, RustConstants.RustFileExtension, },
    supportedTypes: new[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<FileReferenceInfo>) },
    priority: ProviderPriority.Highest)]
public class RustScannerFactory : IWorkspaceProviderFactory<IFileScanner>
{
    public const string ProviderType = "F5628EAD-0000-4683-B597-D8314B971ED6";
    public static readonly Guid ProviderTypeGuid = new (ProviderType);
    private readonly ILogger _logger;
    private ITelemetryService _telemetryService;

    [ImportingConstructor]
    public RustScannerFactory(ILogger logger)
    {
        _logger = logger;
    }

    public IFileScanner CreateProvider(IWorkspace workspaceContext)
    {
        _telemetryService = workspaceContext.GetService<ITelemetryService>();
        _telemetryService.TrackEvent(
            "Create Scanner",
            new[] { ("Location", workspaceContext.Location) });
        _logger.WriteLine("Creating {0}.", GetType().Name);

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
        var parentCargoManifest = _workspace.GetParentCargoManifest(filePath);
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
            var ret = GetFileReferenceInfos(parentCargoManifest, filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileReferenceInfo>)ret);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    private List<FileDataValue> GetFileDataValues(CargoManifest parentCargoManifest, string filePath)
    {
        var allFileDataValues = new List<FileDataValue>();

        if (RustHelpers.IsCargoFile(filePath))
        {
            IPropertySettings launchSettings = new PropertySettings
            {
                [LaunchConfigurationConstants.NameKey] = parentCargoManifest.StartupProjectEntryName,
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
                        target: parentCargoManifest.GetTargetPathForProfile(profile),
                        context: profile),
                });

        allFileDataValues.AddRange(fileDataValuesForAllProfiles);

        return allFileDataValues;
    }

    private static List<FileReferenceInfo> GetFileReferenceInfos(CargoManifest parentCargoManifest, string filePath)
    {
        return parentCargoManifest.Profiles
            .Select(
                profile => new FileReferenceInfo(
                    parentCargoManifest.GetTargetPathForProfileRelativeToPath(profile, filePath),
                    parentCargoManifest.GetTargetPathForProfile(profile),
                    profile,
                    (int)FileReferenceInfoType.Output))
            .ToList();
    }
}
