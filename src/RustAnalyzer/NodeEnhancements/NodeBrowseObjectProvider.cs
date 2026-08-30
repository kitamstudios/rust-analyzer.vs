using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.UI;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.NodeEnhancements;

[Export(typeof(INodeBrowseObjectProvider))]
public sealed class NodeBrowseObjectProvider : INodeBrowseObjectProvider
{
    private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private readonly TL _tl;
    private NodeBrowseObjectPropertyFilter<NodeBrowseObject> _browseObject;

    [ImportingConstructor]
    public NodeBrowseObjectProvider(
        [Import] ITelemetryService t,
        [Import] ILogger l,
        [Import] PrerequisiteAvailabilityPolicy availabilityPolicy)
    {
        _availabilityPolicy = availabilityPolicy;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public object ProvideBrowseObject(WorkspaceVisualNodeBase node)
    {
        if (!_availabilityPolicy.IsReady(AutomaticRustPath.NodeBrowseObject))
        {
            return null;
        }

        var browseObject = GetBrowseObject();
        _tl.L.WriteLine("Getting browse object for {0}.", node.NodeFullMoniker);

        if (node is not IFileSystemNode fsNode || !File.Exists(fsNode.FullPath))
        {
            return null;
        }

        var fullPath = (PathEx)fsNode.FullPath;
        if (!fullPath.IsRustFile() && !fullPath.IsManifest())
        {
            return null;
        }

        if (browseObject.Object.FullPath != default && browseObject.Object.FullPath == fullPath)
        {
            return browseObject;
        }

        var mds = node.Workspace.GetService<IMetadataService>();
        var (hasTargets, isExe) = node.Workspace.JTF.Run(async () => await mds.GetTargetInfoAsync(fullPath, default));
        browseObject.Reset(fullPath, node.Workspace.GetService<ISettingsService>(), hasTargets, isExe, fullPath.IsManifest());
        return browseObject;
    }

    private NodeBrowseObjectPropertyFilter<NodeBrowseObject> GetBrowseObject()
    {
        if (_browseObject == null)
        {
            _browseObject = new NodeBrowseObjectPropertyFilter<NodeBrowseObject>(new NodeBrowseObject());
            _browseObject.Object.PropertyChanged += BrowseObject_PropertyChanged;
        }

        return _browseObject;
    }

    private void BrowseObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (!_availabilityPolicy.IsReady(AutomaticRustPath.NodeBrowseObject))
        {
            return;
        }

        if (sender is not NodeBrowseObject fsob)
        {
            return;
        }

        var update = RustAnalyzerPackage.JTF
            .RunAsync(
                async () =>
                {
                    var val = (string)fsob.GetType().GetProperty(e.PropertyName).GetValue(fsob, null);

                    // NOTE: Trying getting the value and ensure it is not null to frontload potential downstream failures.
                    Ensure.That(SettingsInfo.Store[e.PropertyName].Getter(val)).IsNotNull();
                    await fsob.SS.SetAsync(e.PropertyName, fsob.FullPath, val);
                });
        ObserveUpdate(update.Task);
    }

    private void ObserveUpdate(Task operation)
    {
        operation.ContinueWith(
                task => ReportUnexpectedFault(
                    "NodeBrowseObjectProvider.BrowseObject_PropertyChanged",
                    task.Exception.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default)
            .Forget();
    }

    private void ReportUnexpectedFault(string operation, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        _tl.L.WriteError(
            "Operation '{0}' failed unexpectedly. Ex: {1}",
            operation,
            exception);
    }
}
