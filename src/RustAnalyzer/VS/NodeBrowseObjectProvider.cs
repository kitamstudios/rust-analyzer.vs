using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace.VSIntegration.UI;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.VS;

[Export(typeof(INodeBrowseObjectProvider))]
public sealed class NodeBrowseObjectProvider : INodeBrowseObjectProvider
{
    private readonly TL _tl;
    private readonly FileSystemBrowseObject _browseObject = new ();
    private readonly ISettingsService _settingsService;

    [ImportingConstructor]
    public NodeBrowseObjectProvider([Import] ISettingsService settingsService, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };

        _browseObject.PropertyChanged += BrowseObject_PropertyChanged;
        _settingsService = settingsService;
    }

    public object ProvideBrowseObject(WorkspaceVisualNodeBase node)
    {
        _tl.L.WriteLine("Getting browse object for {0}.", node.NodeFullMoniker);
        if (node is not IFileSystemNode fsNode || !File.Exists(fsNode.FullPath))
        {
            return null;
        }

        var relativePath = PathExtensions.MakeRelativePath(node.Workspace.Location, fsNode.FullPath);
        if (fsNode.FullPath.CanHaveExecutableTargets(node.Workspace.Location))
        {
            var cmdLineArgs = _settingsService.Get(SettingsService.KindDebugger, SettingsService.TypeCmdLineArgs, relativePath);
            _browseObject.ResetForNewNode(relativePath, cmdLineArgs);
            return _browseObject;
        }

        return null;
    }

    private void BrowseObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is FileSystemBrowseObject fsob)
        {
            ThreadHelper.JoinableTaskFactory
                .RunAsync(() => SaveCmdLineArgsToSettingsAsync(fsob.RelativePath, fsob.CommandLineArguments))
                .Forget();
        }
    }

    private Task SaveCmdLineArgsToSettingsAsync(string relativePath, string cmdLineArgs)
    {
        return _settingsService.SetAsync(SettingsService.KindDebugger, SettingsService.TypeCmdLineArgs, relativePath, cmdLineArgs);
    }

    public class FileSystemBrowseObject : INotifyPropertyChanged
    {
        private string _commandLineArguments;

        public event PropertyChangedEventHandler PropertyChanged;

        [Browsable(false)]
        public string RelativePath { get; set; }

        [Category(SettingsService.KindDebugger)]
        [DisplayName("Command line arguments")]
        [Description("Arguments passed to the debugger for F5 & CTRL+F5.")]
        public string CommandLineArguments
        {
            get
            {
                return _commandLineArguments;
            }

            set
            {
                if (value != _commandLineArguments)
                {
                    _commandLineArguments = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public void ResetForNewNode(string relativePath, string cmdLineArgs)
        {
            RelativePath = relativePath;
            _commandLineArguments = cmdLineArgs;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}