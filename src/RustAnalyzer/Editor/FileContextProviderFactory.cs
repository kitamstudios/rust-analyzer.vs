using System;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.Editor;

[ExportFileContextProvider(
    type: ProviderType,
    priority: ProviderPriority.Normal,
    supportedGetContextsTypes: new[] { typeof(string) },
    supportedContextTypeGuids: new[] { BuildContextTypes.BuildContextType, BuildContextTypes.CleanContextType, })]
public sealed class FileContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider>
{
    public const string ProviderType = "72D3FCEF-0001-4266-B8DD-D3ED06E35A2B";

    public static readonly Guid ProviderTypeGuid = new(ProviderType);

    [Import]
    public IBuildOutputSink OutputPane { get; set; }

    [Import]
    public LegacyLogger L { get; set; }

    [Import]
    public ILoggerFactory LoggerFactory { get; set; }

    [Import]
    public Lazy<IToolchainService> LazyCargoService { get; set; }

    public IToolchainService CargoService { get; set; }

    [Import]
    public PrerequisiteAvailabilityPolicy AvailabilityPolicy { get; set; }

    public IFileContextProvider CreateProvider(IWorkspace workspaceContext)
    {
        var provider = new FileContextProvider(
            () => workspaceContext.GetService<IMetadataService>(),
            () => LazyCargoService?.Value ?? CargoService,
            OutputPane,
            () => workspaceContext.GetService<ISettingsService>(),
            AvailabilityPolicy);
        if (!AvailabilityPolicy.IsReady(AutomaticRustPath.OpenFolderContextDiscovery))
        {
            return provider;
        }

        var logger = LoggerFactory?.CreateLogger(
            typeof(FileContextProviderFactory).FullName)
            ?? LegacyLoggerBridge.ToMelLogger(L);
        logger.LogInformation(
            new EventId(1, "ProviderCreated"),
            "Creating {ProviderType}.",
            GetType().Name);

        return provider;
    }
}
