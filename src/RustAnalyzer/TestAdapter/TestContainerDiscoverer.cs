using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

// TODO: Reset test containers when profile changes
[Export(typeof(ITestContainerDiscoverer))]
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
        // TODO: refactor method to SRP.
        // TODO: softcode profile.
        var packages = await mds.GetCachedPackagesAsync(default);
        foreach (var (c, _) in packages.SelectMany(p => p.GetTestContainers("dev")))
        {
            if (!_testContainersCache.TryAdd(c, new TestContainer(c, this, _tl)))
            {
                _tl.L.WriteError("Failed to add '{0}'", c);
            }
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);

        mds.PackageAdded += PackageAddedEventHandler;
        mds.PackageRemoved += PackageRemovedEventHandler;
        mds.TestContainerUpdated += TestContainerUpdatedEventHandler;
    }

    private void UnloadOldWorkspace()
    {
        _tl.L.WriteLine("Unloading workspace at '{0}'.", _currentWorkspace?.Location);
        if (_currentWorkspace == null)
        {
            return;
        }

        var mds = _currentWorkspace.GetService<IMetadataService>();
        mds.TestContainerUpdated -= TestContainerUpdatedEventHandler;
        mds.PackageRemoved -= PackageRemovedEventHandler;
        mds.PackageAdded -= PackageAddedEventHandler;
    }

    // TODO: softcode profile.
    private void PackageRemovedEventHandler(object sender, Workspace.Package e)
    {
        foreach (var (container, _) in e.GetTestContainers("dev"))
        {
            if (!_testContainersCache.TryRemove(container, out _))
            {
                _tl.L.WriteError("Failed to remove container {0}.", container);
            }
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void PackageAddedEventHandler(object sender, Workspace.Package e)
    {
        foreach (var (container, _) in e.GetTestContainers("dev"))
        {
            if (!_testContainersCache.TryAdd(container, new TestContainer(container, this, _tl)))
            {
                _tl.L.WriteError("Failed to add container {0}.", container);
            }
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void TestContainerUpdatedEventHandler(object sender, PathEx e)
    {
        // TODO: implement
        // TODO: handle deleted case.
        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }
}
