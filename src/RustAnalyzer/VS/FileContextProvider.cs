using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.VS;

public sealed class FileContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly string _workspaceRoot;
    private readonly IBuildOutputSink _outputPane;
    private readonly ITelemetryService _t;
    private readonly ILogger _l;

    public FileContextProvider(string workspaceRoot, IBuildOutputSink outputPane, ITelemetryService t, ILogger l)
    {
        _workspaceRoot = workspaceRoot;
        _outputPane = outputPane;
        _t = t;
        _l = l;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        // TODO: BUG: What is context?
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var parentManifest = Manifest.GetParentManifestOrThisUnderWorkspace(_workspaceRoot, filePath);
        if (parentManifest == null)
        {
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        var target = parentManifest.Targets.Where(t => t.Source.Equals(filePath, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (target == null)
        {
            var message = string.Format("Could not find a target for {0}. FileContextProvider is out of sync with FileScanner.", filePath);
            _l.WriteError(message);
            _t.TrackException(new InvalidOperationException(message));
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        return parentManifest.Profiles.SelectMany(p => GetBuildActions(parentManifest, target, p)).ToList();
    }

    private IEnumerable<FileContext> GetBuildActions(Manifest parentManifest, Target target, string profile)
    {
        var action = new[]
        {
            new FileContext(
                providerType: FileContextProviderFactory.ProviderTypeGuid,
                contextType: BuildContextTypes.BuildContextTypeGuid,
                context: new BuildFileContext(profile, parentManifest, target.Manifest.FullPath, target.AdditionalBuildArgs, _outputPane, _t, ShowMessageBoxAsync, _l),
                inputFiles: new[] { target.Source },
                displayName: profile),
        };

        if (target.Type == TargetType.Bin)
        {
            action.Concat(
                new[]
                {
                    new FileContext(
                        providerType: FileContextProviderFactory.ProviderTypeGuid,
                        contextType: BuildContextTypes.CleanContextTypeGuid,
                        context: new CleanFileContext(profile, parentManifest, target.Manifest.FullPath, _outputPane, _t, ShowMessageBoxAsync, _l),
                        inputFiles: new[] { target.Source },
                        displayName: profile),
                });
        }

        return action;
    }

    private async Task ShowMessageBoxAsync(string message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        MessageBox.Show(message, "rust-analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
