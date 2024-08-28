using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

public sealed class MetadataServiceTests
{
    [Theory]
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
