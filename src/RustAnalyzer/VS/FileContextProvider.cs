using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Cargo;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.VS;

public sealed class FileContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly string _workspaceRoot;

    public FileContextProvider(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var parentCargoManifest = Manifest.GetParentManifest(_workspaceRoot, filePath);
        if (parentCargoManifest == null)
        {
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        return parentCargoManifest.Profiles
            .SelectMany(
                profile => new[]
                {
                    new FileContext(
                        FileContextProviderFactory.ProviderTypeGuid,
                        BuildContextTypes.BuildContextTypeGuid,
                        new BuildConfigurationContext(profile),
                        new[] { filePath },
                        displayName: profile),
                    new FileContext(
                        FileContextProviderFactory.ProviderTypeGuid,
                        BuildContextTypes.CleanContextTypeGuid,
                        new BuildConfigurationContext(profile),
                        new[] { filePath },
                        displayName: profile),
                })
            .ToList();
    }
}
