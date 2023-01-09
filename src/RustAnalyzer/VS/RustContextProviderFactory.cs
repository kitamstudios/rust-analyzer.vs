using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.VS;

[ExportFileContextProvider(
    type: ProviderType,
    priority: ProviderPriority.Normal,
    supportedGetContextsTypes: new[] { typeof(string) },
    supportedContextTypeGuids: new[] { BuildContextTypes.BuildContextType, BuildContextTypes.CleanContextType, })]
public sealed class RustContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider>
{
    public static readonly Guid ProviderTypeGuid = new (ProviderType);

    private const string ProviderType = "{72D3FCEF-0000-4266-B8DD-D3ED06E35A2B}";

    public IFileContextProvider CreateProvider(IWorkspace workspaceContext)
    {
        return new RustContextProvider(workspaceContext);
    }
}

public sealed class RustContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly IWorkspace _workspace;

    public RustContextProvider(IWorkspace workspace)
    {
        _workspace = workspace;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var parentCargoManifest = await _workspace.GetParentCargoManifestAsync(filePath);
        if (parentCargoManifest == null)
        {
            return FileContext.EmptyFileContexts;
        }

        return parentCargoManifest.Profiles
            .SelectMany(
                profile => new[]
                {
                    new FileContext(
                        RustContextProviderFactory.ProviderTypeGuid,
                        BuildContextTypes.BuildContextTypeGuid,
                        new BuildConfigurationContext(profile),
                        new[] { filePath },
                        displayName: profile),
                    new FileContext(
                        RustContextProviderFactory.ProviderTypeGuid,
                        BuildContextTypes.CleanContextTypeGuid,
                        new BuildConfigurationContext(profile),
                        new[] { filePath },
                        displayName: profile),
                })
            .ToList();
    }

    private async Task<bool> IsSupportedFileAsync(string filePath)
    {
        if (RustHelpers.IsCargoFile(filePath))
        {
            return true;
        }

        if (RustHelpers.IsRustFile(filePath))
        {
            if (await _workspace.GetParentCargoManifestAsync(filePath) != null)
            {
                return true;
            }
        }

        return false;
    }
}
