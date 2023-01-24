using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

[Export(typeof(ITestContainerDiscoverer))]
public sealed class TestContainerDiscoverer : ITestContainerDiscoverer
{
    private readonly IWorkspace _workspace;
    private readonly ConcurrentDictionary<string, TestContainer> _testContainersCache
        = new (StringComparer.OrdinalIgnoreCase);

    [ImportingConstructor]
    public TestContainerDiscoverer([Import] IVsWorkspaceFactory workspaceFactory)
    {
        _workspace = workspaceFactory.CurrentWorkspace;
        _workspace.GetFileWatcherService().OnFileSystemChanged += FileSystemChangedEventHandlerAsync;
        _ = _workspace.JTF.RunAsync(OnFirstTimeLoadAsync);
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
        if (!Manifest.IsManifest(path))
        {
            return;
        }

        if (changeType.HasFlag(WatcherChangeTypes.Deleted) || changeType.HasFlag(WatcherChangeTypes.Renamed))
        {
            if (_testContainersCache.TryRemove(path, out _))
            {
                TestContainersUpdated?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        if (_testContainersCache.TryAdd(path, new TestContainer(path, new FileInfo(path).LastWriteTime, this, L, T)))
        {
            TestContainersUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private Task FileSystemChangedEventHandlerAsync(object sender, FileSystemEventArgs eventArgs)
    {
        TryUpdateTestContainersCache(eventArgs.FullPath, eventArgs.ChangeType);
        return Task.CompletedTask;
    }

    private async Task OnFirstTimeLoadAsync()
    {
        T.TrackEvent("LoadTestContainerDiscoverer");
        await _workspace.GetFindFilesService().FindFilesAsync("Cargo.toml", new FindFilesProgress(TryUpdateTestContainersCache), CancellationToken.None);
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
