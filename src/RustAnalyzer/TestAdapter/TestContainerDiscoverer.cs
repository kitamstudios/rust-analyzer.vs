using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

[Export(typeof(ITestContainerDiscoverer))]
public sealed class TestContainerDiscoverer : ITestContainerDiscoverer
{
    private readonly ConcurrentDictionary<string, TestContainer> _testContainersCache
        = new (StringComparer.OrdinalIgnoreCase);

    private readonly IVsFolderWorkspaceService _workspaceFactory;
    private IWorkspace _currentWorkspace;

    [ImportingConstructor]
    public TestContainerDiscoverer([Import] SVsServiceProvider serviceProvider)
    {
        _workspaceFactory = serviceProvider
            .GetService<SComponentModel, IComponentModel>()
            .GetService<IVsFolderWorkspaceService>();
        _workspaceFactory.OnActiveWorkspaceChanged += ActiveWorkspaceChangedEventHandlerAsync;
        _currentWorkspace = _workspaceFactory.CurrentWorkspace;
    }

    public event EventHandler TestContainersUpdated;

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    public Uri ExecutorUri => new (Constants.ExecutorUriString);

    public IEnumerable<ITestContainer> TestContainers => _testContainersCache.Values;

    private void TryUpdateTestContainersCache(string path, WatcherChangeTypes changeType = WatcherChangeTypes.Created)
    {
        Ensure.That(Manifest.IsManifest(path)).IsTrue();

        if (!File.Exists(path))
        {
            var removed = _testContainersCache.TryRemove(path, out _);
            if (!removed)
            {
                L.WriteError("Failed to remove container {0}.", path);
            }

            TestContainersUpdated?.Invoke(this, EventArgs.Empty);
        }

        if (!_testContainersCache.ContainsKey(path))
        {
            var added = _testContainersCache.TryAdd(path, new TestContainer(path, this, L, T));
            if (!added)
            {
                L.WriteError("Failed to add container {0}.", path);
            }
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }

    private bool IsPathInDirectory(string location, string path)
    {
        return new FileInfo(path).FullName.ToUpperInvariant().StartsWith(new DirectoryInfo(location).FullName.ToUpperInvariant());
    }

    private async Task ActiveWorkspaceChangedEventHandlerAsync(object sender, EventArgs eventArgs)
    {
        _testContainersCache.Clear();

        L.WriteLine("Unloading workspace at {0}", _currentWorkspace?.Location);
        if (_currentWorkspace != null)
        {
            _currentWorkspace.GetFileWatcherService().OnBatchFileSystemChanged -= BatchFileSystemChangedEventHandlerAsync;
        }

        if (_workspaceFactory.CurrentWorkspace == null)
        {
            return;
        }

        _currentWorkspace = _workspaceFactory.CurrentWorkspace;
        L.WriteLine("TestContainerDiscoverer loading new workspace at {0}", _currentWorkspace.Location);
        T.TrackEvent("TestContainerDiscoverer.LoadWorkspace", ("Location", _currentWorkspace.Location));
        _currentWorkspace.GetFileWatcherService().OnBatchFileSystemChanged += BatchFileSystemChangedEventHandlerAsync;
        await _currentWorkspace.GetFindFilesService().FindFilesAsync(Constants.ManifestFileName, new FindFilesProgress(TryUpdateTestContainersCache));
    }

    private Task BatchFileSystemChangedEventHandlerAsync(object sender, BatchFileSystemEventArgs eventArgs)
    {
        foreach (var fsea in eventArgs.FileSystemEvents.Where(CanFileChangeTests))
        {
            TryUpdateTestContainersCache(fsea.FullPath, fsea.ChangeType);
        }

        return Task.CompletedTask;
    }

    private bool CanFileChangeTests(FileSystemEventArgs eventArgs)
    {
        // TODO: We need to expand the check to include .rs and possibly other files as well.
        return Manifest.IsManifest(eventArgs.FullPath) && IsPathInDirectory(_currentWorkspace.Location, eventArgs.FullPath);
    }

    public class FindFilesProgress : IProgress<string>
    {
        private readonly Action<string, WatcherChangeTypes> _tryUpdateTestContainersCache;

        public FindFilesProgress(Action<string, WatcherChangeTypes> tryUpdateTestContainersCache)
        {
            _tryUpdateTestContainersCache = tryUpdateTestContainersCache;
        }

        public void Report(string value)
        {
            _tryUpdateTestContainersCache(value, WatcherChangeTypes.Created);
        }
    }
}
