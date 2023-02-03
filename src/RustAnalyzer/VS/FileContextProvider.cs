using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.VS;

public sealed class FileContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly string _workspaceRoot;
    private readonly IBuildOutputSink _outputPane;
    private readonly TL _tl;

    public FileContextProvider(string workspaceRoot, IBuildOutputSink outputPane, TL tl)
    {
        _workspaceRoot = workspaceRoot;
        _outputPane = outputPane;
        _tl = tl;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var parentManifest = Manifest.GetParentManifestOrThisUnderWorkspace(_workspaceRoot, filePath);
        if (parentManifest == null)
        {
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        if (filePath.IsManifest())
        {
            return parentManifest.Profiles
                .SelectMany(
                    profile => new[]
                    {
                        new FileContext(
                            FileContextProviderFactory.ProviderTypeGuid,
                            BuildContextTypes.BuildContextTypeGuid,
                            new BuildFileContext(new BuildTargetInfo { Profile = profile, WorkspaceRoot = _workspaceRoot, FilePath = filePath }, _outputPane, VsCommon.ShowMessageBoxAsync, _tl),
                            new[] { filePath },
                            displayName: profile),
                        new FileContext(
                            FileContextProviderFactory.ProviderTypeGuid,
                            BuildContextTypes.CleanContextTypeGuid,
                            new CleanFileContext(new BuildTargetInfo { Profile = profile, WorkspaceRoot = _workspaceRoot, FilePath = filePath }, _outputPane, VsCommon.ShowMessageBoxAsync, _tl),
                            new[] { filePath },
                            displayName: profile),
                    })
                .ToList();
        }
        else if (filePath.IsRustFile())
        {
            var target = parentManifest.Targets.Where(t => t.Source.Equals(filePath, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (target != null)
            {
                return parentManifest.Profiles.SelectMany(p => GetBuildActions(target, p)).ToList();
            }
        }

        return FileContext.EmptyFileContexts;
    }

    private IEnumerable<FileContext> GetBuildActions(Target target, string profile)
    {
        var action = new[]
        {
            new FileContext(
                providerType: FileContextProviderFactory.ProviderTypeGuid,
                contextType: BuildContextTypes.BuildContextTypeGuid,
                context: new BuildFileContext(new BuildTargetInfo { Profile = profile, WorkspaceRoot = _workspaceRoot, FilePath = target.Manifest.FullPath, AdditionalBuildArgs = target.AdditionalBuildArgs }, _outputPane, VsCommon.ShowMessageBoxAsync, _tl),
                inputFiles: new[] { target.Source },
                displayName: profile),
        };

        return action;
    }
}
