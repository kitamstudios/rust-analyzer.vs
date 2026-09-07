using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public sealed class MetadataServiceTests
{
    [Theory]
    [Trait("type", "IntegrationTests")]
    [InlineData(@"hello_library", "hello_lib")]
    [InlineData(@"hello_world", "hello_world")]
    public async Task WorkspaceWithoutRootCargoManifestAsync(string workspaceRelRoot, string packageName)
    {
        var workspaceRoot = TestHelpers.ThisTestRoot;
        var manifestPath = workspaceRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var mds = TestHelpers.MS(workspaceRoot);

        var p = await mds.GetPackageAsync(manifestPath, default);

        p.Name.Should().Be(packageName);
    }

    [Theory]
    [Trait("type", "IntegrationTests")]
    [InlineData("corrupted_manifest")]
    [InlineData("corrupted_manifest2")]
    public async Task InvalidOperationExceptionOnInvalidManifestAsync(string workspaceRelRoot)
    {
        var manifestPath = TestHelpers.ThisTestRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        var mds = TestHelpers.MS(manifestPath.GetDirectoryName());

        Func<Task<Workspace.Package>> action = () => mds.GetPackageAsync(manifestPath, default);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [Trait("type", "UnitTests")]
    [InlineData(@"hello_world")]
    public async Task PackagesShouldBeCachedAsync(string workspaceRelRoot)
    {
        var workspaceRoot = TestHelpers.ThisTestRoot;
        var manifestPath = workspaceRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        CreateMDS(workspaceRoot, manifestPath, out var cs, out var mds);

        await mds.GetPackageAsync(manifestPath, default);
        await mds.GetPackageAsync(manifestPath, default);
        cs.Verify(x => x.GetWorkspaceAsync(manifestPath, default), Times.Once); // NOTE: Just 1 calls to ICS for 2 MDS calls.
    }

    [Theory]
    [Trait("type", "UnitTests")]
    [InlineData(@"hello_world")]
    public async Task PackagesCacheShouldBeInvalidatedAsync(string workspaceRelRoot)
    {
        var workspaceRoot = TestHelpers.ThisTestRoot;
        var manifestPath = workspaceRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        CreateMDS(workspaceRoot, manifestPath, out var cs, out var mds);
        using var mMds = mds.Monitor();

        await mds.GetPackageAsync(manifestPath, default);
        await mds.GetPackageAsync(manifestPath, default);
        await mds.OnWorkspaceUpdateAsync(new[] { manifestPath.GetDirectoryName().Combine((PathEx)"src/main.rs") }, default);
        await mds.GetPackageAsync(manifestPath, default);
        cs.Verify(x => x.GetWorkspaceAsync(manifestPath, default), Times.Exactly(2)); // NOTE: Just 2 calls to ICS for 3 MDS calls.
    }

    [Theory]
    [Trait("type", "UnitTests")]
    [InlineData(@"hello_world")]
    public async Task PackagesAddedRemovedEventsGetFiredAsync(string workspaceRelRoot)
    {
        var workspaceRoot = TestHelpers.ThisTestRoot;
        var manifestPath = workspaceRoot.Combine((PathEx)workspaceRelRoot, Constants.ManifestFileName2);

        CreateTestableMDS(workspaceRoot, manifestPath, out var cs, out var mds);
        using var mMds = mds.Monitor();

        await mds.GetPackageAsync(manifestPath, default);
        mMds.Should().Raise(nameof(IMetadataService.PackageAdded)).WithArgs<Workspace.Package>(p => p.ManifestPath == manifestPath);
        mMds.Clear();

        await mds.GetPackageAsync(manifestPath, default);
        mMds.Should().NotRaise(nameof(IMetadataService.PackageAdded));
        mMds.Clear();

        await mds.OnWorkspaceUpdateAsync(new[] { manifestPath.GetDirectoryName().Combine((PathEx)"src/main.rs") }, default);
        mMds.Should().Raise(nameof(IMetadataService.PackageRemoved)).WithArgs<Workspace.Package>(p => p.ManifestPath == manifestPath);
        mMds.Clear();

        await mds.OnWorkspaceUpdateAsync(new[] { manifestPath.GetDirectoryName().Combine((PathEx)"src/main.rs") }, default);
        mMds.Should().NotRaise(nameof(IMetadataService.PackageRemoved));
        mMds.Clear();

        await mds.GetPackageAsync(manifestPath, default);
        mMds.Should().Raise(nameof(IMetadataService.PackageAdded)).WithArgs<Workspace.Package>(p => p.ManifestPath == manifestPath);
    }

    [Theory]
    [Trait("type", "UnitTests")]
    [InlineData(@"hello_world")]
    public async Task TestContainerUpdatedEventsGetFiredAsync(string workspaceRelRoot)
    {
        var workspaceRoot = TestHelpers.ThisTestRoot;
        var manifestPath = workspaceRoot + (PathEx)workspaceRelRoot + Constants.ManifestFileName2;

        CreateTestableMDS(workspaceRoot, manifestPath, out var cs, out var mds);
        using var mMds = mds.Monitor();

        var testContainer = manifestPath.GetDirectoryName() + (PathEx)$"target/debug/a.{Constants.TestsContainerExtension}";
        await mds.OnWorkspaceUpdateAsync(new[] { testContainer }, default);
        mMds.Should().Raise(nameof(IMetadataService.TestContainerUpdated)).WithArgs<PathEx>(p => p == testContainer);
        mMds.Clear();
    }

    [Fact]
    [Trait("type", "UnitTests")]
    public async Task LifecycleLogsUseStructuredMetadataCategoryAsync()
    {
        var workspaceRoot =
            TestHelpers.ThisTestRoot.Combine((PathEx)"hello_world");
        var manifestPath = workspaceRoot + Constants.ManifestFileName2;
        var cargoService = new Mock<IToolchainService>();
        cargoService
            .Setup(
                service => service.GetWorkspaceAsync(
                    manifestPath,
                    It.IsAny<CancellationToken>()))
            .Returns(CreateWorkspace(manifestPath).ToTask());
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        var service = new MetadataService(
            cargoService.Object,
            workspaceRoot,
            factory.CreateLogger(typeof(MetadataService).FullName));

        await service.GetPackageAsync(manifestPath, default);
        await service.GetPackageAsync(manifestPath, default);
        await service.OnWorkspaceUpdateAsync(
            new[]
            {
                workspaceRoot.Combine(
                    (PathEx)"src",
                    (PathEx)"main.rs"),
            },
            default);
        service.Dispose();

        var entries = provider.Entries.ToArray();
        entries.Select(entry => entry.EventId).Should().Equal(
            new EventId(1, "MetadataServiceCreated"),
            new EventId(2, "PackageRequested"),
            new EventId(6, "PackageCacheMiss"),
            new EventId(2, "PackageRequested"),
            new EventId(5, "PackageCacheEntryRemoved"),
            new EventId(7, "MetadataServiceDisposing"));
        entries.Should().OnlyContain(
            entry => entry.Category == typeof(MetadataService).FullName
                && entry.Level == LogLevel.Information);
        var created = entries.Single(entry => entry.EventId.Id == 1);
        created.Template.Should().Be(
            "Creating MDS. Workspace root: {WorkspaceRoot}.");
        created.Properties["WorkspaceRoot"].Should().Be(workspaceRoot);
        created.Exception.Should().BeNull();
        foreach (var requested in entries.Where(
                     entry => entry.EventId.Id == 2))
        {
            requested.Template.Should().Be(
                "GetPackageAsync. Manifest path: {ManifestPath}.");
            requested.Properties["ManifestPath"].Should().Be(manifestPath);
            requested.Exception.Should().BeNull();
        }

        var cacheMiss = entries.Single(entry => entry.EventId.Id == 6);
        cacheMiss.Template.Should().Be("... Cache miss: {ManifestPath}.");
        cacheMiss.Properties["ManifestPath"].Should().Be(manifestPath);
        cacheMiss.Exception.Should().BeNull();
        var cacheEntryRemoved = entries.Single(
            entry => entry.EventId.Id == 5);
        cacheEntryRemoved.Template.Should().Be(
            "OnWorkspaceUpdateAsync: Removing from cache: {ManifestPath}");
        cacheEntryRemoved.Properties["ManifestPath"].Should().Be(manifestPath);
        cacheEntryRemoved.Exception.Should().BeNull();
        var disposing = entries.Single(entry => entry.EventId.Id == 7);
        disposing.Template.Should().Be(
            "Disposing MDS. Package cache has {PackageCount} entries.");
        disposing.Properties["PackageCount"].Should().Be(0);
        disposing.Exception.Should().BeNull();
        cargoService.Verify(
            service => service.GetWorkspaceAsync(
                manifestPath,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("type", "UnitTests")]
    public async Task EventDispatchFailureIsLoggedAsynchronouslyAsync()
    {
        var expected = new InvalidOperationException("handler failed");
        using var provider = new RecordingLoggerProvider();
        using var factory = new LoggerFactory(new[] { provider, });
        using var service = new MetadataService(
            Mock.Of<IToolchainService>(),
            TestHelpers.ThisTestRoot,
            factory.CreateLogger(typeof(MetadataService).FullName));
        service.TestContainerUpdated += (_, _) => throw expected;

        await service.OnWorkspaceUpdateAsync(
            new[]
            {
                TestHelpers.ThisTestRoot.Combine(
                    (PathEx)"target",
                    (PathEx)$"test.{Constants.TestsContainerExtension}"),
            },
            default);

        SpinWait.SpinUntil(
                () => provider.Entries.Any(
                    entry => entry.EventId.Id == 8),
                TimeSpan.FromSeconds(5))
            .Should().BeTrue();
        var entry = provider.Entries.Single(
            logEntry => logEntry.EventId.Id == 8);
        entry.Category.Should().Be(typeof(MetadataService).FullName);
        entry.Level.Should().Be(LogLevel.Error);
        entry.EventId.Name.Should().Be("EventDispatchFailed");
        entry.Template.Should().Be(
            "Operation '{Operation}' failed unexpectedly.");
        entry.Properties["Operation"].Should().Be(
            "MetadataService.TestContainerUpdatedDispatch");
        entry.Exception.Should().BeSameAs(expected);
    }

    private static void CreateMDS(PathEx workspaceRoot, PathEx manifestPath, out Mock<IToolchainService> cs, out IMetadataService mds)
    {
        cs = new Mock<IToolchainService>();
        cs.Setup(cs => cs.GetWorkspaceAsync(It.IsAny<PathEx>(), It.IsAny<CancellationToken>()))
            .Returns(CreateWorkspace(manifestPath).ToTask());
        mds = new MetadataService(cs.Object, workspaceRoot, TestHelpers.TL);
    }

    private static void CreateTestableMDS(PathEx workspaceRoot, PathEx manifestPath, out Mock<IToolchainService> cs, out IMetadataService mds)
    {
        cs = new Mock<IToolchainService>();
        cs.Setup(cs => cs.GetWorkspaceAsync(It.IsAny<PathEx>(), It.IsAny<CancellationToken>()))
            .Returns(CreateWorkspace(manifestPath).ToTask());
        mds = new TestableMDS(cs.Object, workspaceRoot, TestHelpers.TL);
    }

    private static Workspace CreateWorkspace(PathEx manifestPath)
    {
        var w = new Workspace();
        w.Packages.Add(new Workspace.Package { ManifestPath = manifestPath });
        return w;
    }

    private class TestableMDS : MetadataService
    {
        public TestableMDS(IToolchainService tcs, PathEx workspaceRoot, TL tl)
            : base(tcs, workspaceRoot, tl, syncEvents: true)
        {
        }
    }
}
