using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.VS;

[ExportFileScanner(
    type: ProviderType,
    language: "Rust",
    supportedFileExtensions: new[] { Constants.CargoFileName, Constants.RustFileExtension, },
    supportedTypes: new[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<FileReferenceInfo>) },
    priority: ProviderPriority.Highest)]
public class FileScannerFactory : IWorkspaceProviderFactory<IFileScanner>
{
    public const string ProviderType = "F5628EAD-0000-4683-B597-D8314B971ED6";
    public static readonly Guid ProviderTypeGuid = new (ProviderType);

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    public IFileScanner CreateProvider(IWorkspace workspaceContext)
    {
        T.TrackEvent(
            "Create Scanner",
            new[] { ("Location", workspaceContext.Location) });
        L.WriteLine("Creating {0}.", GetType().Name);

        return new FileScanner(workspaceContext);
    }
}
