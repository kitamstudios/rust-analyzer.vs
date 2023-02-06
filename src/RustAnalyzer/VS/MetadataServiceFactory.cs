using System.ComponentModel.Composition;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.VS;

[ExportWorkspaceServiceFactory(WorkspaceServiceFactoryOptions.None, typeof(IMetadataService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetadataServiceFactory : IWorkspaceServiceFactory
{
    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ICargoService CargoService { get; set; }

    public object CreateService(IWorkspace workspaceContext)
    {
        // TODO: MS: Wireup file changed event handlers
        return new MetadataService(CargoService, (PathEx)workspaceContext.Location, new TL { T = T, L = L, });
    }
}
