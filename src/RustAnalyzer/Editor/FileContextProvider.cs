using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.Editor;

public sealed class FileContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly IMetadataService _mds;
    private readonly ICargoService _cargoService;
    private readonly IBuildOutputSink _outputPane;

    public FileContextProvider(IMetadataService mds, ICargoService cargoService, IBuildOutputSink outputPane)
    {
        _mds = mds;
        _cargoService = cargoService;
        _outputPane = outputPane;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fp = (PathEx)filePath;
        var package = await _mds.GetContainingPackageAsync(fp, cancellationToken);
        if (package == null)
        {
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        if (fp.IsManifest())
        {
            return package.GetProfiles()
                .SelectMany(
                    profile => new[]
                    {
                        new FileContext(
                            FileContextProviderFactory.ProviderTypeGuid,
                            BuildContextTypes.BuildContextTypeGuid,
                            new BuildFileContext(_cargoService, new BuildTargetInfo { Profile = profile, WorkspaceRoot = package.WorkspaceRoot, FilePath = fp }, _outputPane),
                            new[] { (string)fp },
                            displayName: profile),
                        new FileContext(
                            FileContextProviderFactory.ProviderTypeGuid,
                            BuildContextTypes.CleanContextTypeGuid,
                            new CleanFileContext(_cargoService, new BuildTargetInfo { Profile = profile, WorkspaceRoot = package.WorkspaceRoot, FilePath = fp }, _outputPane),
                            new[] { (string)fp },
                            displayName: profile),
                    })
                .ToList();
        }
        else if (fp.IsRustFile())
        {
            var target = package.GetTargets().Where(t => t.SourcePath == fp && t.IsRunnable).FirstOrDefault();
            if (target != null)
            {
                return package.GetProfiles().SelectMany(p => GetBuildActions(target, p)).ToList();
            }
        }

        return FileContext.EmptyFileContexts;
    }

    private IEnumerable<FileContext> GetBuildActions(Workspace.Target target, string profile)
    {
        var action = new[]
        {
            new FileContext(
                providerType: FileContextProviderFactory.ProviderTypeGuid,
                contextType: BuildContextTypes.BuildContextTypeGuid,
                context: new BuildFileContext(_cargoService, new BuildTargetInfo { Profile = profile, WorkspaceRoot = target.Parent.WorkspaceRoot, FilePath = target.Parent.ManifestPath, AdditionalBuildArgs = target.AdditionalBuildArgs }, _outputPane),
                inputFiles: new[] { (string)target.SourcePath },
                displayName: profile),
        };

        return action;
    }
}
