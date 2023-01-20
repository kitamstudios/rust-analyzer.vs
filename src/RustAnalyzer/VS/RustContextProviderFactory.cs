using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Common;
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
    private readonly ILogger _logger;
    private readonly ITelemetryService _telemetryService;

    [ImportingConstructor]
    public RustContextProviderFactory(ILogger logger, ITelemetryService telemetryService)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public IFileContextProvider CreateProvider(IWorkspace workspaceContext)
    {
        _telemetryService.TrackEvent(
            "Create Context Provider",
            new[] { ("Location", workspaceContext.Location) });
        _logger.WriteLine("Creating {0}.", GetType().Name);

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
}
