using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KS.RustAnalyzer.Cargo;
using KS.RustAnalyzer.Common;
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
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var parentManifest = Manifest.GetParentManifest(_workspaceRoot, filePath);
        if (parentManifest == null)
        {
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        return parentManifest.Profiles
            .SelectMany(
                profile => new[]
                {
                    new FileContext(
                        FileContextProviderFactory.ProviderTypeGuid,
                        BuildContextTypes.BuildContextTypeGuid,
                        new BuildFileContext(profile, parentManifest, filePath, _outputPane, _t, ShowMessageBoxAsync, _l),
                        new[] { filePath },
                        displayName: profile),
                    new FileContext(
                        FileContextProviderFactory.ProviderTypeGuid,
                        BuildContextTypes.CleanContextTypeGuid,
                        new CleanFileContext(profile, parentManifest, filePath, _outputPane, _t, ShowMessageBoxAsync, _l),
                        new[] { filePath },
                        displayName: profile),
                })
            .ToList();
    }

    private async Task ShowMessageBoxAsync(string message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        MessageBox.Show(message, "rust-analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
