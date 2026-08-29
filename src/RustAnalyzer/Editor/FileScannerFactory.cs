using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.Editor;

[ExportFileScanner(
    type: ProviderType,
    language: "Rust",
    supportedFileExtensions: new[] { Constants.ManifestFileName, Constants.RustFileExtension, },
    supportedTypes: new[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<FileReferenceInfo>) },
    priority: ProviderPriority.Normal)]
public class FileScannerFactory : IWorkspaceProviderFactory<IFileScanner>
{
    public const string ProviderType = "F5628EAD-0001-4683-B597-D8314B971ED6";

    public static readonly Guid ProviderTypeGuid = new(ProviderType);

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public PrerequisiteAvailabilityPolicy AvailabilityPolicy { get; set; }

    public IFileScanner CreateProvider(IWorkspace workspaceContext)
    {
        var scanner = new FileScanner(
            () => workspaceContext.GetService<IMetadataService>(),
            AvailabilityPolicy);
        if (!AvailabilityPolicy.IsReady(AutomaticRustPath.WorkspaceFileScanning))
        {
            return scanner;
        }

        T.TrackEvent(
            "Create Scanner",
            new[] { ("Location", workspaceContext.Location) });
        L.WriteLine("Creating {0}.", GetType().Name);

        return scanner;
    }
}
