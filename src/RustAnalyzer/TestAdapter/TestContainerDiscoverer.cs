using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

// TODO: UT: [Export(typeof(ITestContainerDiscoverer))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TestContainerDiscoverer : ITestContainerDiscoverer
{
    private readonly ConcurrentDictionary<PathEx, TestContainer> _testContainersCache = new ();

    private readonly IVsFolderWorkspaceService _workspaceFactory;
    private readonly TL _tl;
    private IWorkspace _currentWorkspace;

    [ImportingConstructor]
    public TestContainerDiscoverer([Import] SVsServiceProvider serviceProvider, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _workspaceFactory = VS.GetRequiredService<SComponentModel, IComponentModel>()
            .GetService<IVsFolderWorkspaceService>();
        _workspaceFactory.OnActiveWorkspaceChanged += ActiveWorkspaceChangedEventHandlerAsync;
        _currentWorkspace = _workspaceFactory.CurrentWorkspace;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public event EventHandler TestContainersUpdated;

    public Uri ExecutorUri => new (Constants.ExecutorUriString);

    public IEnumerable<ITestContainer> TestContainers => _testContainersCache.Values;

    private async Task ActiveWorkspaceChangedEventHandlerAsync(object sender, EventArgs eventArgs)
    {
        _testContainersCache.Clear();

        UnloadOldWorkspace();

        await LoadNewWorkspaceAsync();
    }

    private async Task LoadNewWorkspaceAsync()
    {
        if (_workspaceFactory.CurrentWorkspace == null)
        {
            return;
        }

        _currentWorkspace = _workspaceFactory.CurrentWorkspace;
        _tl.L.WriteLine("TestContainerDiscoverer loading new workspace at '{0}'.", _currentWorkspace.Location);
        _tl.T.TrackEvent("TcdLoadWorkspace", ("Location", _currentWorkspace.Location));
        var mds = _currentWorkspace.GetService<IMetadataService>();
        foreach (var p in await mds.GetCachedPackagesAsync(default))
        {
            if (!_testContainersCache.TryAdd(p, new TestContainer(p, this, _tl)))
            {
                _tl.L.WriteError("Failed to add '{0}'", p);
            }
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);

        mds.PackageAdded += PackageAddedEventHandler;
        mds.PackageRemoved += PackageRemovedEventHandler;
    }

    private void UnloadOldWorkspace()
    {
        _tl.L.WriteLine("Unloading workspace at '{0}'.", _currentWorkspace?.Location);
        if (_currentWorkspace == null)
        {
            return;
        }

        var mds = _currentWorkspace.GetService<IMetadataService>();
        mds.PackageRemoved -= PackageRemovedEventHandler;
        mds.PackageAdded -= PackageAddedEventHandler;
    }

    private void PackageRemovedEventHandler(object sender, PathEx e)
    {
        if (!_testContainersCache.TryRemove(e, out _))
        {
            _tl.L.WriteError("Failed to remove container {0}.", e);
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void PackageAddedEventHandler(object sender, PathEx e)
    {
        if (!_testContainersCache.TryAdd(e, new TestContainer(e, this, _tl)))
        {
            _tl.L.WriteError("Failed to add container {0}.", e);
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }
}
