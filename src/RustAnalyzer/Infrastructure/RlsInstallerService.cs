using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LegacyLogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;
using MelLogger = Microsoft.Extensions.Logging.ILogger;

namespace KS.RustAnalyzer.Infrastructure;

public interface IRlsInstallerService
{
    Task<PathEx> GetExePathAsync();

    Task<PathEx> GetExePathAsync(CancellationToken cancellationToken);

    Task InstallLatestAsync();

    Task InstallLatestAsync(CancellationToken cancellationToken);

    bool IsPackagedExePath(PathEx path);

    Task<PathEx> ResetToPackagedAsync();

    Task<PathEx> ResetToPackagedAsync(CancellationToken cancellationToken);
}

public enum RlsReleaseLookupFailure
{
    Unavailable,
    RateLimited,
    Malformed,
}

public class RlsReleaseLookupException : Exception
{
    public RlsReleaseLookupException(string message, Exception innerException)
        : this(RlsReleaseLookupFailure.Unavailable, message, innerException)
    {
    }

    public RlsReleaseLookupException(
        RlsReleaseLookupFailure failure,
        string message,
        Exception innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public RlsReleaseLookupFailure Failure { get; }
}

[Export(typeof(IRlsInstallerService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class RlsInstallerService : IRlsInstallerService
{
    public const string VersionFormat = "yyyy-MM-dd";
    private const string Target = ToolchainServiceExtensions.AlwaysAvailableTarget;
    private const string AssetName = "rust-analyzer-" + Target + ".zip";
    private const string ExecutableName = "rust-analyzer.exe";
    private const string ManifestName = "rust-analyzer.provenance.json";
    private const string PdbName = "rust_analyzer.pdb";
    private const string Repository = "https://github.com/rust-lang/rust-analyzer";
    private const string LatestReleaseUrl = "https://api.github.com/repos/rust-lang/rust-analyzer/releases/latest";
    private static readonly TimeSpan InstallationLockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
    private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private readonly MelLogger _logger;
    private readonly IRegistrySettingsService _registry;

    [ImportingConstructor]
    public RlsInstallerService(
        IRegistrySettingsService registry,
        [Import] ILoggerFactory loggerFactory,
        [Import] PrerequisiteAvailabilityPolicy availabilityPolicy)
        : this(
            registry,
            loggerFactory.CreateLogger(typeof(RlsInstallerService).FullName),
            availabilityPolicy)
    {
    }

    public RlsInstallerService(
        IRegistrySettingsService registry,
        LegacyLogger logger,
        PrerequisiteAvailabilityPolicy availabilityPolicy)
        : this(
            registry,
            LegacyLoggerBridge.ToMelLogger(logger),
            availabilityPolicy)
    {
    }

    private RlsInstallerService(
        IRegistrySettingsService registry,
        MelLogger logger,
        PrerequisiteAvailabilityPolicy availabilityPolicy)
    {
        _registry = registry;
        _logger = logger;
        _availabilityPolicy = availabilityPolicy;
    }

    protected virtual string InstallationRoot => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    protected virtual TimeSpan InstallationWaitTimeout => InstallationLockTimeout;

    public Task InstallLatestAsync()
    {
        return InstallLatestAsync(default);
    }

    public async Task InstallLatestAsync(CancellationToken cancellationToken)
    {
        if (!await _availabilityPolicy.IsReadyAsync(
                AutomaticRustPath.RustAnalyzerUpdaterDownload,
                cancellationToken))
        {
            return;
        }

        _logger.LogInformation(
            new EventId(1, "RustAnalyzerUpdateCheckStarted"),
            "Checking for a rust-analyzer update.");
        try
        {
            var release = await ResolveLatestReleaseAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.CompareOrdinal(release.Version, Constants.RlsLatestInPackageVersion) <= 0)
            {
                _logger.LogInformation(
                    new EventId(2, "PackagedRustAnalyzerCurrent"),
                    "Packaged rust-analyzer {Version} is current.",
                    Constants.RlsLatestInPackageVersion);
                return;
            }

            await Task.Run(
                    () => InstallLatestUnderLockAsync(
                        release,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RlsReleaseLookupException e)
        {
            await ResetAfterUpdateFailureAsync(cancellationToken)
                .ConfigureAwait(false);
            _logger.LogError(
                new EventId(3, "RustAnalyzerUpdateMetadataFailed"),
                e,
                "Rust-analyzer update metadata failed ({Failure}); using the packaged version.",
                e.Failure);
        }
        catch (Exception e)
        {
            await ResetAfterUpdateFailureAsync(cancellationToken)
                .ConfigureAwait(false);
            _logger.LogError(
                new EventId(4, "RustAnalyzerUpdateFailed"),
                e,
                "Rust-analyzer update failed; using the packaged version.");
        }
    }

    public Task<PathEx> GetExePathAsync()
    {
        return GetExePathAsync(default);
    }

    public async Task<PathEx> GetExePathAsync(CancellationToken cancellationToken)
    {
        var packagedPath = GetPackagedExePath();
        try
        {
            return await Task.Run(
                    () => GetExePathUnderLockAsync(
                        packagedPath,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException e)
        {
            _logger.LogError(
                new EventId(5, "SelectedRustAnalyzerValidationTimedOut"),
                e,
                "Timed out waiting to validate the selected rust-analyzer; using the packaged version.");
            return packagedPath;
        }
        catch (Exception e)
        {
            _logger.LogError(
                new EventId(6, "SelectedRustAnalyzerValidationFailed"),
                e,
                "Failed to validate the selected rust-analyzer; using the packaged version.");
            return packagedPath;
        }
    }

    public bool IsPackagedExePath(PathEx path)
    {
        return string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(GetPackagedExePath()),
            StringComparison.OrdinalIgnoreCase);
    }

    public Task<PathEx> ResetToPackagedAsync()
    {
        return ResetToPackagedAsync(default);
    }

    public async Task<PathEx> ResetToPackagedAsync(
        CancellationToken cancellationToken)
    {
        await Task.Run(
                async () =>
                {
                    using (await AcquireInstallationLockAsync(
                            cancellationToken)
                        .ConfigureAwait(false))
                    {
                        await WriteSelectedVersionAsync(
                                Constants.RlsLatestInPackageVersion,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

        return GetPackagedExePath();
    }

    public static async Task<(Uri Uri, string Version)> GetLatestRlsReleaseRedirectUriAsync()
    {
        var release = await ResolveLatestReleaseCoreAsync(default);
        return (release.AssetUri, release.Version);
    }

    protected virtual async Task<IDisposable> AcquireInstallationLockAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(InstallationRoot);
        var lockPath = Path.Combine(
            InstallationRoot,
            ".rust-analyzer-install.lock");
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < InstallationWaitTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                var remaining = InstallationWaitTimeout - wait.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(
                        remaining < TimeSpan.FromMilliseconds(50)
                            ? remaining
                            : TimeSpan.FromMilliseconds(50),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            "Timed out waiting for another Visual Studio process to finish installing rust-analyzer.");
    }

    protected virtual async Task DownloadArchiveAsync(
        ReleaseInfo release,
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(NetworkTimeout);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rust-analyzer.vs-runtime");
        using var response = await client.GetAsync(
            release.AssetUri,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync();
        using var output = File.Create(archivePath);
        await input.CopyToAsync(output, 81920, timeout.Token);
    }

    protected virtual string GetVersionDirectory(string version)
    {
        return Path.Combine(InstallationRoot, version);
    }

    protected virtual async Task<string> ReadSelectedVersionAsync(
        CancellationToken cancellationToken)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync(
            cancellationToken);
        return _registry.InstalledRustAnalyzerVersion;
    }

    protected virtual async Task<string> ReadVersionAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            Arguments = "--version",
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        var exited = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult(null);
        if (!process.Start())
        {
            throw new InvalidDataException(
                "rust-analyzer --version did not start.");
        }

        if (process.HasExited)
        {
            exited.TrySetResult(null);
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = cancellationToken.Register(
            () =>
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                }

                exited.TrySetCanceled();
            });
        var timeout = Task.Delay(VersionTimeout);
        var completed = await Task.WhenAny(exited.Task, timeout)
            .ConfigureAwait(false);
        if (completed == timeout)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "rust-analyzer --version timed out.");
        }

        await exited.Task.ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
        var standardOutput = await output.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                "rust-analyzer --version failed.");
        }

        var version = standardOutput.TrimEnd('\r', '\n');
        if (!Regex.IsMatch(
                version,
                @"^rust-analyzer \S+ \([0-9a-f]+ \d{4}-\d{2}-\d{2}\)$",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException(
                "rust-analyzer --version returned an invalid value.");
        }

        return version;
    }

    protected virtual Task<ReleaseInfo> ResolveLatestReleaseAsync(
        CancellationToken cancellationToken)
    {
        return ResolveLatestReleaseCoreAsync(cancellationToken);
    }

    protected virtual async Task WriteSelectedVersionAsync(
        string version,
        CancellationToken cancellationToken)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync(
            cancellationToken);
        _registry.InstalledRustAnalyzerVersion = version;
    }

    protected virtual async Task EnableUpdateNotificationAsync(
        CancellationToken cancellationToken)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync(
            cancellationToken);
        RlsUpdatedNotification.Enabled = true;
    }

    protected sealed class ReleaseInfo
    {
        public ReleaseInfo(
            string version,
            Uri assetUri,
            string officialSha256,
            Uri officialDigestSource)
        {
            Version = version;
            AssetUri = assetUri;
            OfficialSha256 = officialSha256;
            OfficialDigestSource = officialDigestSource;
        }

        public string Version { get; }

        public Uri AssetUri { get; }

        public string OfficialSha256 { get; }

        public Uri OfficialDigestSource { get; }
    }

    private async Task InstallLatestUnderLockAsync(
        ReleaseInfo release,
        CancellationToken cancellationToken)
    {
        var installationLock = await AcquireInstallationLockAsync(
                cancellationToken)
            .ConfigureAwait(false);
        using (installationLock)
        {
            try
            {
                var selectedVersion = await ReadSelectedVersionAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var targetDirectory = GetVersionDirectory(release.Version);
                if (Directory.Exists(targetDirectory))
                {
                    var validTarget = false;
                    try
                    {
                        await ValidateVersionDirectoryAsync(
                                targetDirectory,
                                release.Version,
                                release.OfficialSha256,
                                cancellationToken)
                            .ConfigureAwait(false);
                        validTarget = true;
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        await WriteSelectedVersionAsync(
                                Constants.RlsLatestInPackageVersion,
                                cancellationToken)
                            .ConfigureAwait(false);
                        _logger.LogInformation(
                            new EventId(7, "IncompleteRustAnalyzerReplacing"),
                            e,
                            "Replacing incomplete rust-analyzer {Version}.",
                            release.Version);
                        Directory.Delete(targetDirectory, true);
                    }

                    if (validTarget)
                    {
                        if (!string.Equals(
                                selectedVersion,
                                release.Version,
                                StringComparison.Ordinal))
                        {
                            await CommitAsync(
                                    release.Version,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }

                        return;
                    }
                }

                Directory.CreateDirectory(targetDirectory);
                await InstallReleaseAsync(
                        release,
                        targetDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                await CommitAsync(release.Version, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                await WriteSelectedVersionAsync(
                        Constants.RlsLatestInPackageVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogError(
                    new EventId(8, "LockedRustAnalyzerUpdateFailed"),
                    e,
                    "Rust-analyzer update failed; using the packaged version.");
            }
        }
    }

    private async Task<PathEx> GetExePathUnderLockAsync(
        PathEx packagedPath,
        CancellationToken cancellationToken)
    {
        var installationLock = await AcquireInstallationLockAsync(
                cancellationToken)
            .ConfigureAwait(false);
        using (installationLock)
        {
            var selectedVersion = await ReadSelectedVersionAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(selectedVersion) ||
                string.Equals(
                    selectedVersion,
                    Constants.RlsLatestInPackageVersion,
                    StringComparison.Ordinal))
            {
                return packagedPath;
            }

            if (!DateTime.TryParseExact(
                    selectedVersion,
                    VersionFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                await WriteSelectedVersionAsync(
                        Constants.RlsLatestInPackageVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
                return packagedPath;
            }

            try
            {
                return await ValidateVersionDirectoryAsync(
                        GetVersionDirectory(selectedVersion),
                        selectedVersion,
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    new EventId(9, "SelectedRustAnalyzerInvalid"),
                    e,
                    "Selected rust-analyzer {Version} is invalid; using the packaged version.",
                    selectedVersion);
                await WriteSelectedVersionAsync(
                        Constants.RlsLatestInPackageVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
                return packagedPath;
            }
        }
    }

    private static async Task<ReleaseInfo> ResolveLatestReleaseCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(NetworkTimeout);
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "rust-analyzer.vs-runtime");
            using var response = await client.GetAsync(
                LatestReleaseUrl,
                HttpCompletionOption.ResponseContentRead,
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.Forbidden ||
                (int)response.StatusCode == 429)
            {
                throw new RlsReleaseLookupException(
                    RlsReleaseLookupFailure.RateLimited,
                    "GitHub rate-limited rust-analyzer release metadata.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new RlsReleaseLookupException(
                    RlsReleaseLookupFailure.Unavailable,
                    "Official rust-analyzer release metadata was unavailable.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParseRelease(await response.Content.ReadAsStringAsync());
        }
        catch (RlsReleaseLookupException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException e)
        {
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Unavailable,
                "Official rust-analyzer release metadata timed out.",
                e);
        }
        catch (HttpRequestException e)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Unavailable,
                "Official rust-analyzer release metadata was unavailable.",
                e);
        }
        catch (Exception e) when (
            e is JsonException ||
            e is FormatException ||
            e is InvalidOperationException ||
            e is UriFormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Malformed,
                "Official rust-analyzer release metadata was malformed.",
                e);
        }
    }

    private static ReleaseInfo ParseRelease(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Malformed,
                "Official rust-analyzer release metadata was empty.");
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException e)
        {
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Malformed,
                "Official rust-analyzer release metadata was malformed.",
                e);
        }

        var release = root.Value<string>("tag_name");
        if (!DateTime.TryParseExact(
                release,
                VersionFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Malformed,
                "Official rust-analyzer release metadata has an invalid tag.");
        }

        var assets = root["assets"]?
            .Children<JObject>()
            .Where(asset => asset.Value<string>("name") == AssetName)
            .ToArray() ?? Array.Empty<JObject>();
        if (assets.Length != 1)
        {
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Malformed,
                "Official rust-analyzer release metadata has an invalid Windows amd64 asset set.");
        }

        var digest = assets[0].Value<string>("digest");
        var digestMatch = Regex.Match(
            digest ?? string.Empty,
            "^sha256:([0-9a-fA-F]{64})$",
            RegexOptions.CultureInvariant);
        var assetUri = CreateHttpsUri(
            assets[0].Value<string>("browser_download_url"));
        var metadataSource = CreateHttpsUri(
            assets[0].Value<string>("url"));
        if (!digestMatch.Success ||
            assetUri == null ||
            metadataSource == null)
        {
            throw new RlsReleaseLookupException(
                RlsReleaseLookupFailure.Malformed,
                "Official rust-analyzer release metadata has invalid digest or URL fields.");
        }

        return new ReleaseInfo(
            release,
            assetUri,
            digestMatch.Groups[1].Value.ToLowerInvariant(),
            metadataSource);
    }

    private async Task CommitAsync(
        string version,
        CancellationToken cancellationToken)
    {
        await WriteSelectedVersionAsync(version, cancellationToken)
            .ConfigureAwait(false);
        await EnableUpdateNotificationAsync(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            new EventId(10, "RustAnalyzerCommitted"),
            "Committed rust-analyzer {Version}.",
            version);
    }

    private async Task ResetAfterUpdateFailureAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(
                    async () =>
                    {
                        var installationLock =
                            await AcquireInstallationLockAsync(
                                    cancellationToken)
                                .ConfigureAwait(false);
                        using (installationLock)
                        {
                            await WriteSelectedVersionAsync(
                                    Constants.RlsLatestInPackageVersion,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(
                new EventId(11, "PackagedRustAnalyzerResetFailed"),
                e,
                "Failed to reset rust-analyzer to the packaged version.");
        }
    }

    private static Uri CreateHttpsUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps
                ? uri
                : null;
    }

    private static string GetSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private async Task InstallReleaseAsync(
        ReleaseInfo release,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(targetDirectory, AssetName);
        try
        {
            await DownloadArchiveAsync(
                    release,
                    archivePath,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var archiveSha256 = GetSha256(archivePath);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(
                    archiveSha256,
                    release.OfficialSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Downloaded rust-analyzer archive digest did not match official metadata.");
            }

            await ExtractArchiveAsync(
                    archivePath,
                    targetDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            var executable = Path.Combine(targetDirectory, ExecutableName);
            var executableSha256 = GetSha256(executable);
            cancellationToken.ThrowIfCancellationRequested();
            var pdb = Path.Combine(targetDirectory, PdbName);
            var pdbSha256 = File.Exists(pdb) ? GetSha256(pdb) : null;
            cancellationToken.ThrowIfCancellationRequested();
            var reportedVersion = await ReadVersionAsync(
                    executable,
                    cancellationToken)
                .ConfigureAwait(false);
            var manifest = new ProvenanceManifest
            {
                SchemaVersion = 1,
                Upstream = new ProvenanceUpstream
                {
                    Repository = Repository,
                    Release = release.Version,
                    Target = Target,
                },
                Assets = new[]
                {
                    new ProvenanceAsset
                    {
                        Name = AssetName,
                        Url = release.AssetUri.AbsoluteUri,
                        OfficialSha256 = release.OfficialSha256,
                        OfficialDigestSource =
                            release.OfficialDigestSource.AbsoluteUri,
                        VerifiedArchiveSha256 = archiveSha256,
                    },
                },
                Files = new ProvenanceFiles
                {
                    Executable = new ProvenanceFile
                    {
                        Name = ExecutableName,
                        Sha256 = executableSha256,
                    },
                    Pdb = pdbSha256 != null
                        ? new ProvenanceFile
                        {
                            Name = PdbName,
                            Sha256 = pdbSha256,
                        }
                        : null,
                },
                ReportedVersion = reportedVersion,
            };
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(
                Path.Combine(targetDirectory, ManifestName),
                JsonConvert.SerializeObject(manifest, Formatting.Indented),
                new UTF8Encoding(false));
        }
        finally
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.ToArray();
        if (entries.Any(entry =>
                entry.FullName != ExecutableName &&
                entry.FullName != PdbName))
        {
            throw new InvalidDataException(
                "Downloaded rust-analyzer archive contains an unexpected or unsafe entry.");
        }

        var executableEntries = entries
            .Where(entry => entry.FullName == ExecutableName)
            .ToArray();
        var pdbEntries = entries
            .Where(entry => entry.FullName == PdbName)
            .ToArray();
        if (executableEntries.Length != 1 || pdbEntries.Length > 1)
        {
            throw new InvalidDataException(
                "Downloaded rust-analyzer archive has missing or duplicate expected entries.");
        }

        foreach (var entry in executableEntries.Concat(pdbEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = entry.Open();
            using var output = File.Create(
                Path.Combine(targetDirectory, entry.FullName));
            await input.CopyToAsync(
                    output,
                    81920,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<PathEx> ValidateVersionDirectoryAsync(
        string directory,
        string expectedRelease,
        string expectedOfficialSha256,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, ManifestName);
        if (File.Exists(Path.Combine(directory, AssetName)) ||
            !File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                "rust-analyzer version directory is incomplete.");
        }

        ProvenanceManifest manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<ProvenanceManifest>(
                File.ReadAllText(manifestPath));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(
                "rust-analyzer provenance manifest is malformed.",
                e);
        }

        if (manifest?.SchemaVersion != 1 ||
            manifest.Upstream?.Repository != Repository ||
            manifest.Upstream.Release != expectedRelease ||
            manifest.Upstream.Target != Target ||
            manifest.Assets?.Length != 1 ||
            manifest.Assets[0]?.Name != AssetName ||
            CreateHttpsUri(manifest.Assets[0].Url) == null ||
            CreateHttpsUri(manifest.Assets[0].OfficialDigestSource) == null ||
            !IsSha256(manifest.Assets[0].OfficialSha256) ||
            !IsSha256(manifest.Assets[0].VerifiedArchiveSha256) ||
            !string.Equals(
                manifest.Assets[0].OfficialSha256,
                manifest.Assets[0].VerifiedArchiveSha256,
                StringComparison.OrdinalIgnoreCase) ||
            (expectedOfficialSha256 != null &&
                !string.Equals(
                    manifest.Assets[0].OfficialSha256,
                    expectedOfficialSha256,
                    StringComparison.OrdinalIgnoreCase)) ||
            manifest.Files?.Executable?.Name != ExecutableName ||
            !IsSha256(manifest.Files.Executable.Sha256) ||
            string.IsNullOrEmpty(manifest.ReportedVersion))
        {
            throw new InvalidDataException(
                "rust-analyzer provenance manifest is invalid.");
        }

        var executable = Path.Combine(directory, ExecutableName);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executable) ||
            !string.Equals(
                GetSha256(executable),
                manifest.Files.Executable.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "rust-analyzer executable hash does not match its provenance manifest.");
        }

        var pdb = Path.Combine(directory, PdbName);
        cancellationToken.ThrowIfCancellationRequested();
        if (manifest.Files.Pdb == null)
        {
            if (File.Exists(pdb))
            {
                throw new InvalidDataException(
                    "rust-analyzer PDB is not recorded in its provenance manifest.");
            }
        }
        else if (manifest.Files.Pdb.Name != PdbName ||
            !IsSha256(manifest.Files.Pdb.Sha256) ||
            !File.Exists(pdb) ||
            !string.Equals(
                GetSha256(pdb),
                manifest.Files.Pdb.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "rust-analyzer PDB does not match its provenance manifest.");
        }

        var reportedVersion = await ReadVersionAsync(
            executable,
            cancellationToken);
        if (!string.Equals(
                reportedVersion,
                manifest.ReportedVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "rust-analyzer version does not match its provenance manifest.");
        }

        return (PathEx)executable;
    }

    private static bool IsSha256(string value)
    {
        return value != null &&
            Regex.IsMatch(
                value,
                "^[0-9a-fA-F]{64}$",
                RegexOptions.CultureInvariant);
    }

    private PathEx GetPackagedExePath()
    {
        return (PathEx)Path.Combine(
            InstallationRoot,
            Constants.RlsLatestInPackageVersion,
            ExecutableName);
    }

    private sealed class ProvenanceManifest
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("upstream")]
        public ProvenanceUpstream Upstream { get; set; }

        [JsonProperty("assets")]
        public ProvenanceAsset[] Assets { get; set; }

        [JsonProperty("files")]
        public ProvenanceFiles Files { get; set; }

        [JsonProperty("reportedVersion")]
        public string ReportedVersion { get; set; }
    }

    private sealed class ProvenanceUpstream
    {
        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("release")]
        public string Release { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }
    }

    private sealed class ProvenanceAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("officialSha256")]
        public string OfficialSha256 { get; set; }

        [JsonProperty("officialDigestSource")]
        public string OfficialDigestSource { get; set; }

        [JsonProperty("verifiedArchiveSha256")]
        public string VerifiedArchiveSha256 { get; set; }
    }

    private sealed class ProvenanceFiles
    {
        [JsonProperty("executable")]
        public ProvenanceFile Executable { get; set; }

        [JsonProperty("pdb", NullValueHandling = NullValueHandling.Ignore)]
        public ProvenanceFile Pdb { get; set; }
    }

    private sealed class ProvenanceFile
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }
    }
}
