using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Win32;

namespace KS.RustAnalyzer.Infrastructure;

public interface IRlsInstallerService
{
    Task<PathEx> GetExePathAsync();

    Task InstallLatestAsync();
}

// The lookup runs in a static with no logger, so it classifies its own failure and lets the instance
// caller decide what it means. Unreachable github, a redirect without a Location header and a release
// tag that is not a date are all the same outcome: the latest release is not knowable right now.
public class RlsReleaseLookupException : Exception
{
    public RlsReleaseLookupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

[Export(typeof(IRlsInstallerService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class RlsInstallerService : IRlsInstallerService
{
    public const string VersionFormat = "yyyy-MM-dd";
    private const string InstalledRlsVersionKey = "InstalledRlsVersion";
    private readonly IRegistrySettingsService _regSettings;
    private readonly TL _tl;

    [ImportingConstructor]
    public RlsInstallerService(IRegistrySettingsService regSettings, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _regSettings = regSettings;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public async Task InstallLatestAsync()
    {
        _tl.L.WriteLine("Initiating download of RLS...");
        try
        {
            var latestRel = await GetLatestRlsReleaseRedirectUriAsync();
            string installedVer = await GetInstalledVersionAsync();
            if (installedVer.CompareTo(latestRel.Version) >= 0)
            {
                _tl.L.WriteLine($"Not going to download RLS. Installed = {installedVer}, Latest = {latestRel.Uri}.");
                _tl.T.TrackEvent("RLSDS.RlsUpToDate", ("Installed", installedVer), ("Latest", latestRel.Uri.ToString()));
                return;
            }

            using var response = await DownloadAsync(latestRel);

            using var zipStream = await response.Content.ReadAsStreamAsync();
            Install(zipStream, latestRel.Version);

            await CommitAsync(latestRel);
            _tl.T.TrackEvent("RLSDS.RlsInstalled", ("Installed", installedVer));
        }
        catch (RlsReleaseLookupException ex)
        {
            // Nothing was downloaded and nothing is broken: the packaged rust-analyzer still works, so
            // this reports what actually happened instead of a download failure that never started.
            _tl.L.WriteError($"Latest release could not be determined; keeping the packaged version. {ex}");
            _tl.T.TrackException(ex);
        }
        catch (Exception ex)
        {
            _tl.L.WriteError($"Download failed. StatusCode {ex}");
            _tl.T.TrackException(ex);
            throw;
        }
    }

    public async Task<PathEx> GetExePathAsync()
    {
        return GetVersionedExePath(await GetInstalledVersionAsync());
    }

    public static async Task<(Uri Uri, string Version)> GetLatestRlsReleaseRedirectUriAsync()
    {
        try
        {
            var latestRelUri = await GetRedirectedUrlAsync("https://github.com/rust-lang/rust-analyzer/releases/latest".ToUri());

            var latestRelVersion = latestRelUri.Segments[latestRelUri.Segments.Length - 1];
            var latestRelDate = DateTime.ParseExact(latestRelVersion, VersionFormat, CultureInfo.InvariantCulture);

            return (Uri: new Uri($"https://github.com/rust-lang/rust-analyzer/releases/download/{latestRelVersion}/rust-analyzer-{ToolchainServiceExtensions.AlwaysAvailableTarget}.zip"),
                Version: latestRelDate.ToString(VersionFormat, CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is InvalidOperationException || e is FormatException || e is UriFormatException)
        {
            throw new RlsReleaseLookupException("Could not determine the latest rust-analyzer release.", e);
        }
    }

    private PathEx GetVersionedExePath(string version)
    {
        return GetInstallFolder(version) + (PathEx)$"rust-analyzer.exe";
    }

    private async Task<HttpResponseMessage> DownloadAsync((Uri Uri, string Version) latestRel)
    {
        _tl.L.WriteLine($"Downloading RLS from {latestRel.Uri}.");
        var response = await new HttpClient().GetAsync(latestRel.Uri);
        if (!response.IsSuccessStatusCode)
        {
            _tl.L.WriteError($"Download failed. StatusCode {response.StatusCode}.");
            _tl.T.TrackEvent("RLSDS.RlsDownloadFailed", ("StatusCode", response.StatusCode.ToString()));
            throw new Exception($"RLSDS.RlsDownloadFailed. {response.StatusCode}.");
        }

        return response;
    }

    private async Task CommitAsync((Uri Uri, string Version) latestRel)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
        if (!_regSettings.GetPackageRegistryRoot(out var regRoot))
        {
            _tl.L.WriteError($"GetPackageRegistryRoot failed.");
            throw new Exception($"GetPackageRegistryRoot failed.");
        }

        Registry.SetValue(regRoot, InstalledRlsVersionKey, latestRel.Version);
        RlsUpdatedNotification.Enabled = true;
        _tl.L.WriteLine($"Committed RLS installation.");
    }

    private void Install(Stream zipStream, string downloadedVersion)
    {
        _tl.L.WriteLine($"Installing RLS v {downloadedVersion}...");
        var raFolder = GetInstallFolder(downloadedVersion);
        Directory.CreateDirectory(raFolder);
        using var zip = new ZipArchive(zipStream);
        foreach (var entry in zip.Entries)
        {
            var dstFile = raFolder + entry.FullName;
            entry.ExtractToFile(dstFile, true);
            _tl.L.WriteLine($"... Installing {dstFile}");
        }
    }

    private async Task<string> GetInstalledVersionAsync()
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();

        var installedRlsVersion = Constants.RlsLatestInPackageVersion;
        if (_regSettings.GetPackageRegistryRoot(out var regRoot))
        {
            installedRlsVersion = Registry.GetValue(regRoot, InstalledRlsVersionKey, null) as string;
        }

        installedRlsVersion ??= Constants.RlsLatestInPackageVersion;
        if (GetVersionedExePath(installedRlsVersion).FileExists())
        {
            return installedRlsVersion;
        }

        return Constants.RlsLatestInPackageVersion;
    }

    private static PathEx GetInstallFolder(string version)
    {
        return (PathEx)Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + (PathEx)version;
    }

    private static async Task<Uri> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, }, true);
        using var response = await client.GetAsync(uri, cancellationToken);

        return new Uri(response.Headers.GetValues("Location").First());
    }
}
