using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

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
    public LegacyLogger L { get; set; }

    [Import]
    public ILoggerFactory LoggerFactory { get; set; }

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

        var logger = LoggerFactory?.CreateLogger(
            typeof(FileScannerFactory).FullName)
            ?? LegacyLoggerBridge.ToMelLogger(L);
        logger.LogInformation(
            new EventId(1, "ProviderCreated"),
            "Creating {ProviderType}.",
            GetType().Name);

        return scanner;
    }
}
