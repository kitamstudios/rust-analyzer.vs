using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Threading;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class RlsInstallerServiceTests
{
    private const string DownloadedRelease = "2026-09-07";
    private const string DownloadedVersion =
        "rust-analyzer 0.3.3034-standalone (2222222 2026-09-06)";

    private const string PreviousRelease = "2026-08-17";

    [Theory]
    [InlineData(RlsReleaseLookupFailure.Unavailable)]
    [InlineData(RlsReleaseLookupFailure.RateLimited)]
    [InlineData(RlsReleaseLookupFailure.Malformed)]
    public async Task MetadataFailureDoesNotInspectOrActivateAsync(
        RlsReleaseLookupFailure failure)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Installer.ReleaseFailure =
            new RlsReleaseLookupException(failure, "metadata failure");

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.LockAcquisitions.Should().Be(1);
        fixture.Installer.Downloads.Should().Be(0);
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        fixture.Installer.PointerWrites.Should().Equal(
            Constants.RlsLatestInPackageVersion);
        fixture.Logger.Errors.Should().ContainSingle();
        string.Format(
                fixture.Logger.Errors[0].Format,
                fixture.Logger.Errors[0].Arguments)
            .Should().Contain(failure.ToString());
    }

    [Fact]
    public async Task CallerCancellationDuringMetadataPropagatesWithoutFailureHandlingAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Installer.SelectedVersion = PreviousRelease;
        fixture.Installer.WaitForMetadataCancellation = true;
        using var cancellation = new CancellationTokenSource();

        var update = fixture.Installer.InstallLatestAsync(
            cancellation.Token);
        await fixture.Installer.MetadataStarted.Task;
        cancellation.Cancel();

        await AssertCanceledAsync(update);
        fixture.Installer.SelectedVersion.Should().Be(PreviousRelease);
        fixture.Installer.PointerWrites.Should().BeEmpty();
        fixture.Installer.LockAcquisitions.Should().Be(0);
        fixture.Logger.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ malformed")]
    [InlineData("{\"tag_name\":\"2026-09-07\",\"assets\":[]}")]
    [InlineData("{\"tag_name\":\"2026-09-07\",\"assets\":[{\"name\":\"rust-analyzer-x86_64-pc-windows-msvc.zip\",\"browser_download_url\":\"https://example.invalid/a.zip\",\"url\":\"https://api.github.com/assets/1\"}]}")]
    public void MalformedOrDigestlessMetadataIsRejected(string json)
    {
        var parse = typeof(RlsInstallerService).GetMethod(
            "ParseRelease",
            BindingFlags.Static | BindingFlags.NonPublic);
        parse.Should().NotBeNull();
        Action action = () => parse.Invoke(null, new object[] { json });

        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<RlsReleaseLookupException>()
            .Which.Failure.Should().Be(RlsReleaseLookupFailure.Malformed);
    }

    [Fact]
    public async Task DigestMismatchDoesNotExtractExecuteOrActivateAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.Installer.OfficialSha256 =
            new string('0', 64);

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.VersionReads.Should().BeEmpty();
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        File.Exists(
            Path.Combine(
                fixture.Installer.VersionDirectory(DownloadedRelease),
                "rust-analyzer.exe")).Should().BeFalse();
    }

    [Fact]
    public async Task InternalDownloadTimeoutSelectsPackagedNonfatallyAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Installer.SelectedVersion = PreviousRelease;
        fixture.Installer.DownloadFailure = new TimeoutException();

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        fixture.Installer.PointerWrites.Should().Equal(
            Constants.RlsLatestInPackageVersion);
        fixture.Logger.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateFailureReplacesPreviouslySelectedDownloadWithPackagedAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateValidVersionDirectory(
            PreviousRelease,
            "previous executable",
            "previous pdb",
            new string('a', 64),
            DownloadedVersion);
        fixture.Installer.SelectedVersion = PreviousRelease;
        (await fixture.Installer.GetExePathAsync()).Should().Be(
            (PathEx)Path.Combine(
                fixture.Installer.VersionDirectory(PreviousRelease),
                "rust-analyzer.exe"));
        fixture.Installer.PointerWrites.Clear();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.Installer.OfficialSha256 = new string('0', 64);

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        fixture.Installer.PointerWrites.Should().Equal(
            Constants.RlsLatestInPackageVersion);
        Directory.Exists(
            fixture.Installer.VersionDirectory(PreviousRelease))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("nested/rust-analyzer.exe")]
    [InlineData("unexpected.txt")]
    public async Task UnsafeOrUnexpectedArchiveEntryIsRejectedAsync(
        string entryName)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"),
            (entryName, "unexpected"));

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.VersionReads.Should().BeEmpty();
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        File.Exists(Path.Combine(fixture.Root, "outside.exe"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateArchiveEntryIsRejectedAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "first"),
            ("rust-analyzer.exe", "second"));

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.VersionReads.Should().BeEmpty();
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
    }

    [Fact]
    public async Task VersionProbeFailureDoesNotActivateDownloadAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.Installer.VersionFailure = new TimeoutException();

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.VersionReads.Should().ContainSingle();
        fixture.Installer.PointerWrites.Should().Equal(
            Constants.RlsLatestInPackageVersion);
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
    }

    [Fact]
    public async Task ValidTargetIsReusedAndOtherCompletedDirectoriesRemainAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.CreateValidVersionDirectory(
            DownloadedRelease,
            "new executable",
            "new pdb",
            fixture.Installer.OfficialSha256,
            DownloadedVersion);
        var inactiveDirectory = Path.Combine(fixture.Root, "2026-08-17");
        Directory.CreateDirectory(inactiveDirectory);
        var inactiveMarker = Path.Combine(inactiveDirectory, "complete.marker");
        File.WriteAllText(inactiveMarker, "keep");

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.Downloads.Should().Be(0);
        fixture.Installer.SelectedVersion.Should().Be(DownloadedRelease);
        File.Exists(inactiveMarker).Should().BeTrue();
        fixture.Installer.PointerObservedValidManifest.Should().BeTrue();
        fixture.Installer.PointerObservedVersionValidation.Should().BeTrue();
    }

    [Fact]
    public async Task PartialTargetAloneIsRecreatedAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        var targetDirectory =
            fixture.Installer.VersionDirectory(DownloadedRelease);
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(
            Path.Combine(targetDirectory, "partial.marker"),
            "partial");
        var completedDirectory = Path.Combine(fixture.Root, "2026-08-17");
        Directory.CreateDirectory(completedDirectory);
        var completedMarker = Path.Combine(
            completedDirectory,
            "complete.marker");
        File.WriteAllText(completedMarker, "keep");

        await fixture.Installer.InstallLatestAsync();
        var selected = await fixture.Installer.GetExePathAsync();

        fixture.Installer.Downloads.Should().Be(1);
        fixture.Installer.SelectedVersion.Should().Be(DownloadedRelease);
        selected.Should().Be(
            (PathEx)Path.Combine(
                targetDirectory,
                "rust-analyzer.exe"));
        File.Exists(Path.Combine(targetDirectory, "partial.marker"))
            .Should().BeFalse();
        File.Exists(
            Path.Combine(
                targetDirectory,
                "rust-analyzer-x86_64-pc-windows-msvc.zip"))
            .Should().BeFalse();
        File.Exists(completedMarker).Should().BeTrue();
        fixture.Installer.PointerObservedValidManifest.Should().BeTrue();
        fixture.Installer.PointerObservedVersionValidation.Should().BeTrue();
    }

    [Fact]
    public async Task LockTimeoutDoesNotInspectOrActivateTargetAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        var targetDirectory =
            fixture.Installer.VersionDirectory(DownloadedRelease);
        Directory.CreateDirectory(targetDirectory);
        var marker = Path.Combine(targetDirectory, "partial.marker");
        File.WriteAllText(marker, "keep");
        fixture.Installer.LockFailure = new TimeoutException();

        await fixture.Installer.InstallLatestAsync();

        File.Exists(marker).Should().BeTrue();
        fixture.Installer.Downloads.Should().Be(0);
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
    }

    [Fact]
    public async Task CallerCancellationDuringLockWaitPropagatesWithoutResetAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Installer.SelectedVersion = PreviousRelease;
        fixture.Installer.WaitForLockCancellation = true;
        using var cancellation = new CancellationTokenSource();

        var update = fixture.Installer.InstallLatestAsync(
            cancellation.Token);
        await fixture.Installer.LockStarted.Task;
        cancellation.Cancel();

        await AssertCanceledAsync(update);
        fixture.Installer.SelectedVersion.Should().Be(PreviousRelease);
        fixture.Installer.PointerWrites.Should().BeEmpty();
        fixture.Logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task InstallationLockSerializesProcessesAndTimesOutAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var policy = new PrerequisiteAvailabilityPolicy(
            state,
            new RecordingLogger());
        var root = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "runtime-lock-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var first = new LockProbeInstaller(root, policy);
            var second = new LockProbeInstaller(root, policy);
            using var held = await first.AcquireAsync();
            Func<Task> acquire = async () =>
            {
                using var ignored = await second.AcquireAsync();
            };

            await acquire.Should().ThrowAsync<TimeoutException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void LockWaitAndLockedFileWorkYieldTheJtfOwnerThread()
    {
        var synchronizationContext =
            new SingleThreadedSynchronizationContext();
        var context = new JoinableTaskContext(
            Thread.CurrentThread,
            synchronizationContext);
        var root = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "runtime-lock-tests",
            Guid.NewGuid().ToString("N"));
        Fixture fixture = null;
        try
        {
            RunOnOwnerThread(
                context,
                synchronizationContext,
                async () =>
                {
                    var state = new PrerequisiteProcessState(context.Factory);
                    await state.GetOrEvaluateAsync(
                        _ => Task.FromResult(PrerequisiteResult.Success),
                        default);
                    var policy = new PrerequisiteAvailabilityPolicy(
                        state,
                        new RecordingLogger());
                    var first = new LockProbeInstaller(root, policy);
                    var second = new LockProbeInstaller(root, policy);
                    var held = await first.AcquireAsync();
                    var waiting = second.AcquireAsync();
                    waiting.IsCompleted.Should().BeFalse();
                    held.Dispose();
                    using var acquired = await waiting;

                    fixture = await Fixture.CreateAsync(context);
                    fixture.CreateArchive(
                        ("rust-analyzer.exe", "new executable"),
                        ("rust_analyzer.pdb", "new pdb"));
                    var ownerThread =
                        Thread.CurrentThread.ManagedThreadId;
                    fixture.Installer.FileWorkThreads.Clear();
                    fixture.Installer.FileWorkGate =
                        new TaskCompletionSource<object>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                    var installation =
                        fixture.Installer.InstallLatestAsync();
                    await fixture.Installer.FileWorkStarted.Task;
                    fixture.Installer.FileWorkThreads.Should().NotContain(
                        ownerThread);
                    fixture.Installer.FileWorkGate.SetResult(null);
                    await installation;
                    fixture.Installer.FileWorkThreads.Should().NotContain(
                        ownerThread);
                });
        }
        finally
        {
            fixture?.Dispose();
            if (fixture == null)
            {
                context.Dispose();
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task PointerCommitFailureRollsBackAfterValidationAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateValidVersionDirectory(
            PreviousRelease,
            "previous executable",
            "previous pdb",
            new string('a', 64),
            DownloadedVersion);
        fixture.Installer.SelectedVersion = PreviousRelease;
        (await fixture.Installer.GetExePathAsync()).Should().Be(
            (PathEx)Path.Combine(
                fixture.Installer.VersionDirectory(PreviousRelease),
                "rust-analyzer.exe"));
        fixture.Installer.PointerWrites.Clear();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.Installer.FailLatestPointerCommit = true;

        await fixture.Installer.InstallLatestAsync();

        fixture.Installer.PointerWrites.Should().Equal(
            DownloadedRelease,
            Constants.RlsLatestInPackageVersion);
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        fixture.Installer.PointerObservedValidManifest.Should().BeTrue();
        fixture.Installer.PointerObservedVersionValidation.Should().BeTrue();
        File.Exists(
            Path.Combine(
                fixture.Installer.VersionDirectory(DownloadedRelease),
                "rust-analyzer.provenance.json")).Should().BeTrue();
        Directory.Exists(
            fixture.Installer.VersionDirectory(PreviousRelease))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CallerCancellationAfterDownloadPreservesPreviousSelectionAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateValidVersionDirectory(
            PreviousRelease,
            "previous executable",
            "previous pdb",
            new string('a', 64),
            DownloadedVersion);
        fixture.Installer.SelectedVersion = PreviousRelease;
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.Installer.WaitAfterDownloadForCancellation = true;
        using var cancellation = new CancellationTokenSource();

        var update = fixture.Installer.InstallLatestAsync(
            cancellation.Token);
        await fixture.Installer.DownloadCompleted.Task;
        cancellation.Cancel();

        await AssertCanceledAsync(update);
        fixture.Installer.SelectedVersion.Should().Be(PreviousRelease);
        fixture.Installer.PointerWrites.Should().BeEmpty();
        fixture.Logger.Errors.Should().BeEmpty();
        Directory.Exists(
            fixture.Installer.VersionDirectory(PreviousRelease))
            .Should().BeTrue();
        File.Exists(
            Path.Combine(
                fixture.Installer.VersionDirectory(DownloadedRelease),
                "rust-analyzer-x86_64-pc-windows-msvc.zip"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ValidSelectedDownloadIsReturnedAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateArchive(
            ("rust-analyzer.exe", "new executable"),
            ("rust_analyzer.pdb", "new pdb"));
        fixture.CreateValidVersionDirectory(
            DownloadedRelease,
            "new executable",
            "new pdb",
            fixture.Installer.OfficialSha256,
            DownloadedVersion);
        fixture.Installer.SelectedVersion = DownloadedRelease;

        var selected = await fixture.Installer.GetExePathAsync();

        selected.Should().Be(
            (PathEx)Path.Combine(
                fixture.Installer.VersionDirectory(DownloadedRelease),
                "rust-analyzer.exe"));
        fixture.Installer.PointerWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("rust-analyzer.exe")]
    [InlineData("rust_analyzer.pdb")]
    public async Task SelectedDownloadHashFailureFallsBackWithoutExecutionOrDeletionAsync(
        string corruptedFile)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateValidVersionDirectory(
            DownloadedRelease,
            "downloaded executable",
            "downloaded pdb",
            new string('a', 64),
            DownloadedVersion);
        var targetDirectory =
            fixture.Installer.VersionDirectory(DownloadedRelease);
        File.WriteAllText(
            Path.Combine(targetDirectory, corruptedFile),
            "corrupted");

        fixture.Installer.SelectedVersion = DownloadedRelease;

        var selected = await fixture.Installer.GetExePathAsync();

        selected.Should().Be(
            (PathEx)Path.Combine(
                fixture.Root,
                Constants.RlsLatestInPackageVersion,
                "rust-analyzer.exe"));
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        Directory.Exists(targetDirectory).Should().BeTrue();
        fixture.Installer.VersionReads.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectedDownloadVersionMismatchFallsBackWithoutDeletionAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateValidVersionDirectory(
            DownloadedRelease,
            "downloaded executable",
            "downloaded pdb",
            new string('a', 64),
            DownloadedVersion);
        var targetDirectory =
            fixture.Installer.VersionDirectory(DownloadedRelease);
        fixture.Installer.ReportedVersion =
            "rust-analyzer 0.3.9999-standalone (9999999 2026-09-06)";
        fixture.Installer.SelectedVersion = DownloadedRelease;

        var selected = await fixture.Installer.GetExePathAsync();

        selected.Should().Be(
            (PathEx)Path.Combine(
                fixture.Root,
                Constants.RlsLatestInPackageVersion,
                "rust-analyzer.exe"));
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        Directory.Exists(targetDirectory).Should().BeTrue();
        fixture.Installer.VersionReads.Should().ContainSingle();
    }

    [Fact]
    public async Task CallerCancellationDuringSelectionValidationPropagatesWithoutResetAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CreateValidVersionDirectory(
            DownloadedRelease,
            "downloaded executable",
            "downloaded pdb",
            new string('a', 64),
            DownloadedVersion);
        fixture.Installer.SelectedVersion = DownloadedRelease;
        fixture.Installer.WaitForVersionCancellation = true;
        using var cancellation = new CancellationTokenSource();

        var selection = fixture.Installer.GetExePathAsync(
            cancellation.Token);
        await fixture.Installer.VersionStarted.Task;
        cancellation.Cancel();

        await AssertCanceledAsync(selection);
        fixture.Installer.SelectedVersion.Should().Be(DownloadedRelease);
        fixture.Installer.PointerWrites.Should().BeEmpty();
        fixture.Logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task CallerCancellationDuringPackagedResetPropagatesWithoutWriteAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Installer.SelectedVersion = PreviousRelease;
        fixture.Installer.WaitForLockCancellation = true;
        using var cancellation = new CancellationTokenSource();

        var reset = fixture.Installer.ResetToPackagedAsync(
            cancellation.Token);
        await fixture.Installer.LockStarted.Task;
        cancellation.Cancel();

        await AssertCanceledAsync(reset);
        fixture.Installer.SelectedVersion.Should().Be(PreviousRelease);
        fixture.Installer.PointerWrites.Should().BeEmpty();
        fixture.Logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacySelectedDownloadFallsBackWithoutDeletionAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        var targetDirectory =
            fixture.Installer.VersionDirectory(DownloadedRelease);
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(
            Path.Combine(targetDirectory, "rust-analyzer.exe"),
            "legacy executable");
        fixture.Installer.SelectedVersion = DownloadedRelease;

        var selected = await fixture.Installer.GetExePathAsync();

        selected.Should().Be(
            (PathEx)Path.Combine(
                fixture.Root,
                Constants.RlsLatestInPackageVersion,
                "rust-analyzer.exe"));
        fixture.Installer.SelectedVersion.Should().Be(
            Constants.RlsLatestInPackageVersion);
        Directory.Exists(targetDirectory).Should().BeTrue();
        fixture.Installer.VersionReads.Should().BeEmpty();
    }

    private static string GetSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static async Task AssertCanceledAsync(Task operation)
    {
        await ((Func<Task>)(async () => await operation))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static void RunOnOwnerThread(
        JoinableTaskContext context,
        SingleThreadedSynchronizationContext synchronizationContext,
        Func<Task> action)
    {
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(
                synchronizationContext);
            var operation = context.Factory.RunAsync(action);
            var frame = new SingleThreadedSynchronizationContext.Frame();
            _ = operation.Task.ContinueWith(
                _ => synchronizationContext.Post(
                    _ => frame.Continue = false,
                    null),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            synchronizationContext.PushFrame(frame);
            operation.Join();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(
                previousContext);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string root,
            JoinableTaskContext context,
            RecordingLogger logger,
            TestInstaller installer)
        {
            Root = root;
            Context = context;
            Logger = logger;
            Installer = installer;
        }

        public string Root { get; }

        public JoinableTaskContext Context { get; }

        public TestInstaller Installer { get; }

        public RecordingLogger Logger { get; }

        public static async Task<Fixture> CreateAsync(
            JoinableTaskContext context = null)
        {
            var root = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "runtime-update-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(
                Path.Combine(root, Constants.RlsLatestInPackageVersion));
            File.WriteAllText(
                Path.Combine(
                    root,
                    Constants.RlsLatestInPackageVersion,
                    "rust-analyzer.exe"),
                "packaged executable");
            context ??= new JoinableTaskContext();
            var state = new PrerequisiteProcessState(context.Factory);
            await state.GetOrEvaluateAsync(
                _ => Task.FromResult(PrerequisiteResult.Success),
                default);
            var logger = new RecordingLogger();
            var installer = new TestInstaller(
                root,
                logger,
                new PrerequisiteAvailabilityPolicy(
                    state,
                    logger));
            return new Fixture(root, context, logger, installer);
        }

        public void CreateArchive(
            params (string Name, string Content)[] entries)
        {
            var archivePath = Path.Combine(Root, "release.zip");
            using (var archive = ZipFile.Open(
                       archivePath,
                       ZipArchiveMode.Create))
            {
                foreach (var (name, content) in entries)
                {
                    var entry = archive.CreateEntry(name);
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(content);
                }
            }

            Installer.ArchivePath = archivePath;
            Installer.OfficialSha256 = GetSha256(archivePath);
        }

        public void CreateValidVersionDirectory(
            string release,
            string executableContent,
            string pdbContent,
            string archiveSha256,
            string reportedVersion)
        {
            var directory = Installer.VersionDirectory(release);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "rust-analyzer.exe"),
                executableContent);
            if (pdbContent != null)
            {
                File.WriteAllText(
                    Path.Combine(directory, "rust_analyzer.pdb"),
                    pdbContent);
            }

            WriteManifest(
                directory,
                release,
                archiveSha256,
                reportedVersion);
        }

        public void WriteManifest(
            string directory,
            string release,
            string archiveSha256,
            string reportedVersion)
        {
            var files = new Dictionary<string, object>
            {
                ["executable"] = new
                {
                    name = "rust-analyzer.exe",
                    sha256 = GetSha256(
                        Path.Combine(directory, "rust-analyzer.exe")),
                },
            };
            if (File.Exists(Path.Combine(directory, "rust_analyzer.pdb")))
            {
                files["pdb"] = new
                {
                    name = "rust_analyzer.pdb",
                    sha256 = GetSha256(
                        Path.Combine(directory, "rust_analyzer.pdb")),
                };
            }

            var manifest = new
            {
                schemaVersion = 1,
                upstream = new
                {
                    repository =
                        "https://github.com/rust-lang/rust-analyzer",
                    release,
                    target = "x86_64-pc-windows-msvc",
                },
                assets = new[]
                {
                    new
                    {
                        name =
                            "rust-analyzer-x86_64-pc-windows-msvc.zip",
                        url =
                            $"https://example.invalid/{release}/rust-analyzer.zip",
                        officialSha256 = archiveSha256,
                        officialDigestSource =
                            "https://api.github.com/repos/rust-lang/rust-analyzer/releases/assets/1",
                        verifiedArchiveSha256 = archiveSha256,
                    },
                },
                files,
                reportedVersion,
            };
            File.WriteAllText(
                Path.Combine(directory, "rust-analyzer.provenance.json"),
                JsonConvert.SerializeObject(manifest));
        }

        public void Dispose()
        {
            Context.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }

    private sealed class TestInstaller : RlsInstallerService
    {
        private readonly string _root;

        public TestInstaller(
            string root,
            ILogger logger,
            PrerequisiteAvailabilityPolicy availabilityPolicy)
            : base(
                Mock.Of<IRegistrySettingsService>(),
                logger,
                availabilityPolicy)
        {
            _root = root;
            SelectedVersion = Constants.RlsLatestInPackageVersion;
        }

        public string ArchivePath { get; set; }

        public int Downloads { get; private set; }

        public Exception DownloadFailure { get; set; }

        public bool FailLatestPointerCommit { get; set; }

        public TaskCompletionSource<object> DownloadCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object> FileWorkGate { get; set; }

        public TaskCompletionSource<object> FileWorkStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> FileWorkThreads { get; } = new();

        public Exception LockFailure { get; set; }

        public int LockAcquisitions { get; private set; }

        public TaskCompletionSource<object> LockStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object> MetadataStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string OfficialSha256 { get; set; } =
            new string('a', 64);

        public bool PointerObservedValidManifest { get; private set; }

        public bool PointerObservedVersionValidation { get; private set; }

        public List<string> PointerWrites { get; } = new();

        public string ReportedVersion { get; set; } = DownloadedVersion;

        public Exception ReleaseFailure { get; set; }

        public string SelectedVersion { get; set; }

        public List<string> VersionReads { get; } = new();

        public Exception VersionFailure { get; set; }

        public TaskCompletionSource<object> VersionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WaitAfterDownloadForCancellation { get; set; }

        public bool WaitForLockCancellation { get; set; }

        public bool WaitForMetadataCancellation { get; set; }

        public bool WaitForVersionCancellation { get; set; }

        public string VersionDirectory(string version)
        {
            return GetVersionDirectory(version);
        }

        protected override string InstallationRoot => _root;

        protected override string GetVersionDirectory(string version)
        {
            FileWorkThreads.Add(Thread.CurrentThread.ManagedThreadId);
            return base.GetVersionDirectory(version);
        }

        protected override async Task<IDisposable> AcquireInstallationLockAsync(
            CancellationToken cancellationToken)
        {
            FileWorkThreads.Add(Thread.CurrentThread.ManagedThreadId);
            LockAcquisitions++;
            LockStarted.TrySetResult(null);
            if (WaitForLockCancellation)
            {
                await Task.Delay(
                        Timeout.Infinite,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (LockFailure != null)
            {
                throw LockFailure;
            }

            return new LockHandle();
        }

        protected override async Task DownloadArchiveAsync(
            ReleaseInfo release,
            string archivePath,
            CancellationToken cancellationToken)
        {
            FileWorkThreads.Add(Thread.CurrentThread.ManagedThreadId);
            FileWorkStarted.TrySetResult(null);
            if (FileWorkGate != null)
            {
                await FileWorkGate.Task.ConfigureAwait(false);
            }

            Downloads++;
            if (DownloadFailure != null)
            {
                throw DownloadFailure;
            }

            File.Copy(ArchivePath, archivePath, true);
            DownloadCompleted.TrySetResult(null);
            if (WaitAfterDownloadForCancellation)
            {
                await Task.Delay(
                        Timeout.Infinite,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        protected override Task EnableUpdateNotificationAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        protected override Task<string> ReadSelectedVersionAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SelectedVersion);
        }

        protected override Task<string> ReadVersionAsync(
            string executable,
            CancellationToken cancellationToken)
        {
            FileWorkThreads.Add(Thread.CurrentThread.ManagedThreadId);
            VersionReads.Add(executable);
            VersionStarted.TrySetResult(null);
            if (WaitForVersionCancellation)
            {
                return WaitForVersionCancellationAsync(
                    cancellationToken);
            }

            if (VersionFailure != null)
            {
                return Task.FromException<string>(VersionFailure);
            }

            return Task.FromResult(ReportedVersion);
        }

        protected override async Task<ReleaseInfo> ResolveLatestReleaseAsync(
            CancellationToken cancellationToken)
        {
            MetadataStarted.TrySetResult(null);
            if (WaitForMetadataCancellation)
            {
                await Task.Delay(
                        Timeout.Infinite,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (ReleaseFailure != null)
            {
                throw ReleaseFailure;
            }

            return new ReleaseInfo(
                DownloadedRelease,
                new Uri(
                    $"https://example.invalid/{DownloadedRelease}/rust-analyzer.zip"),
                OfficialSha256,
                new Uri(
                    "https://api.github.com/repos/rust-lang/rust-analyzer/releases/assets/2"));
        }

        protected override Task WriteSelectedVersionAsync(
            string version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PointerWrites.Add(version);
            SelectedVersion = version;
            if (version == DownloadedRelease)
            {
                PointerObservedValidManifest = File.Exists(
                    Path.Combine(
                        VersionDirectory(version),
                        "rust-analyzer.provenance.json"));
                PointerObservedVersionValidation = VersionReads.Any(
                    path => path == Path.Combine(
                        VersionDirectory(version),
                        "rust-analyzer.exe"));
                if (FailLatestPointerCommit)
                {
                    FailLatestPointerCommit = false;
                    throw new InvalidOperationException(
                        "Synthetic pointer commit failure.");
                }
            }

            return Task.CompletedTask;
        }

        private async Task<string> WaitForVersionCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(
                    Timeout.Infinite,
                    cancellationToken)
                .ConfigureAwait(false);
            return ReportedVersion;
        }

        private sealed class LockHandle : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class LockProbeInstaller : RlsInstallerService
    {
        public LockProbeInstaller(
            string root,
            PrerequisiteAvailabilityPolicy availabilityPolicy)
            : base(
                Mock.Of<IRegistrySettingsService>(),
                new RecordingLogger(),
                availabilityPolicy)
        {
            InstallationRoot = root;
        }

        public Task<IDisposable> AcquireAsync()
        {
            return AcquireInstallationLockAsync(default);
        }

        protected override TimeSpan InstallationWaitTimeout =>
            TimeSpan.FromMilliseconds(100);

        protected override string InstallationRoot { get; }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(string Format, object[] Arguments)> Errors { get; } =
            new();

        public List<(string Format, object[] Arguments)> Lines { get; } =
            new();

        public void WriteError(string format, params object[] args)
        {
            Errors.Add((format, args));
        }

        public void WriteLine(string format, params object[] args)
        {
            Lines.Add((format, args));
        }
    }
}
