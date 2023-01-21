using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.VS;

public sealed class FileContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly IWorkspace _workspace;

    public FileContextProvider(IWorkspace workspace)
    {
        _workspace = workspace;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var parentCargoManifest = _workspace.GetParentCargoManifest(filePath);
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
