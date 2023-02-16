using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.Infrastructure;

[ExportWorkspaceServiceFactory(WorkspaceServiceFactoryOptions.None, typeof(IMetadataService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetadataServiceFactory : IWorkspaceServiceFactory
{
    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public IToolChainService CargoService { get; set; }

    public object CreateService(IWorkspace workspaceContext)
    {
        var mds = new MetadataService(
            CargoService,
            (PathEx)workspaceContext.Location,
            new TL { T = T, L = L, });

        Func<object, BatchFileSystemEventArgs, Task> eh = async (_, e) => await BatchFileSystemChangedEventHandlerAsync(e, mds);
        workspaceContext.GetFileWatcherService().OnBatchFileSystemChanged += eh;
        mds.DisconnectEvents = () => { workspaceContext.GetFileWatcherService().OnBatchFileSystemChanged -= eh; };

        return mds;
    }

    private async Task BatchFileSystemChangedEventHandlerAsync(BatchFileSystemEventArgs eventArgs, IMetadataService mds)
    {
        var filePaths = eventArgs.FileSystemEvents.Select(fse => (PathEx?)fse.FullPath).Where(x => x.HasValue).Select(x => x.Value).Distinct();
        await mds.OnWorkspaceUpdateAsync(filePaths, default);
    }
}
