using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
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
    private readonly NodeBrowseObject _browseObject = new ();
    private readonly IPreReqsCheckService _preReqs;

    [ImportingConstructor]
    public NodeBrowseObjectProvider([Import] IPreReqsCheckService preReqs, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };

        _browseObject.PropertyChanged += BrowseObject_PropertyChanged;
        _preReqs = preReqs;
    }

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

        var mds = node.Workspace.GetService<IMetadataService>();
        var ss = node.Workspace.GetService<ISettingsService>();
        if (node.Workspace.JTF.Run(async () => await mds.CanHaveExecutableTargetsAsync((PathEx)fsNode.FullPath, default)))
        {
            var fullPath = (PathEx)fsNode.FullPath;
            _browseObject.ResetForNewNode(
                fullPath,
                ss,
                ss.Get(SettingsService.TypeCommandLineArguments, fullPath),
                ss.Get(SettingsService.TypeDebuggerEnvironment, fullPath),
                ss.Get(SettingsService.TypeAdditionalBuildArgs, fullPath));
            return _browseObject;
        }

        return null;
    }

    private void BrowseObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not NodeBrowseObject fsob)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory
            .RunAsync(() => fsob.SS.SetAsync(e.PropertyName, (PathEx)fsob.RelativePath, (string)fsob.GetType().GetProperty(e.PropertyName).GetValue(fsob, null)))
            .Forget();
    }
}
