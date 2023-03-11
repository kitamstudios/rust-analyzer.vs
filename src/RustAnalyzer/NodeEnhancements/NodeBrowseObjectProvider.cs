using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
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
    private readonly TL _tl;
    private readonly NodeBrowseObjectPropertyFilter<NodeBrowseObject> _browseObject = new (new ());
    private readonly IPreReqsCheckService _preReqs;

    [ImportingConstructor]
    public NodeBrowseObjectProvider([Import] IPreReqsCheckService preReqs, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };

        _browseObject.Object.PropertyChanged += BrowseObject_PropertyChanged;
        _preReqs = preReqs;
    }

    // TODO: RELEASE: Unit test this.
    public object ProvideBrowseObject(WorkspaceVisualNodeBase node)
    {
        _tl.L.WriteLine("Getting browse object for {0}.", node.NodeFullMoniker);

        if (!node.Workspace.JTF.Run(() => _preReqs.SatisfySilentAsync()))
        {
            _tl.L.WriteLine("... Pre-requisites not satisfied. Returning null.");
            return null;
        }

        if (node is not IFileSystemNode fsNode || !File.Exists(fsNode.FullPath))
        {
            return null;
        }

        var fullPath = (PathEx)fsNode.FullPath;
        if (!fullPath.IsRustFile() && !fullPath.IsManifest())
        {
            return null;
        }

        if (_browseObject.Object.FullPath != default && _browseObject.Object.FullPath == fullPath)
        {
            return _browseObject;
        }

        var mds = node.Workspace.GetService<IMetadataService>();
        var (hasTargets, isExe) = node.Workspace.JTF.Run(async () => await mds.GetTargetInfoAsync(fullPath, default));
        _browseObject.Reset(fullPath, node.Workspace.GetService<ISettingsService>(), hasTargets, isExe, fullPath.IsManifest());
        return _browseObject;
    }

    private void BrowseObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not NodeBrowseObject fsob)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory
            .RunAsync(
                async () =>
                {
                    var val = (string)fsob.GetType().GetProperty(e.PropertyName).GetValue(fsob, null);

                    // NOTE: Trying getting the value and ensure it is not null to frontload potential downstream failures.
                    Ensure.That(SettingsService.PropertyInfo[e.PropertyName].Getter(val)).IsNotNull();
                    await fsob.SS.SetAsync(e.PropertyName, fsob.FullPath, val);
                })
            .FireAndForget();
    }
}
