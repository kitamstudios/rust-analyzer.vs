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
            if (latestRel != null && installedVer.CompareTo(latestRel?.Version) >= 0)
            {
                _tl.L.WriteLine($"Not going to download RLS. Installed = {installedVer}, Latest = {latestRel?.Uri}.");
                _tl.T.TrackEvent("RLSDS.RlsUpToDate", ("Installed", installedVer), ("Latest", latestRel?.Uri.ToString()));
                return;
            }

            using var response = await DownloadAsync(latestRel);

            using var zipStream = await response.Content.ReadAsStreamAsync();
            Install(zipStream, latestRel?.Version);

            await CommitAsync(latestRel);
            _tl.T.TrackEvent("RLSDS.RlsInstalled", ("Installed", installedVer));
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

    public static async Task<(Uri Uri, string Version)?> GetLatestRlsReleaseRedirectUriAsync()
    {
        try
        {
            var latestRelUri = await GetRedirectedUrlAsync("https://github.com/rust-lang/rust-analyzer/releases/latest".ToUri());

            var latestRelVersion = latestRelUri.Segments[latestRelUri.Segments.Length - 1];
            var latestRelDate = DateTime.ParseExact(latestRelVersion, VersionFormat, CultureInfo.InvariantCulture);

            return (Uri: new Uri($"https://github.com/rust-lang/rust-analyzer/releases/download/{latestRelVersion}/rust-analyzer-{ToolChainServiceExtensions.AlwaysAvailableTarget}.zip"),
                Version: latestRelDate.ToString(VersionFormat, CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private PathEx GetVersionedExePath(string version)
    {
        return GetInstallFolder(version) + (PathEx)$"rust-analyzer.exe";
    }

    private async Task<HttpResponseMessage> DownloadAsync((Uri Uri, string Version)? latestRel)
    {
        _tl.L.WriteLine($"Downloading RLS from {latestRel?.Uri}.");
        var response = await new HttpClient().GetAsync(latestRel?.Uri);
        if (!response.IsSuccessStatusCode)
        {
            _tl.L.WriteError($"Download failed. StatusCode {response.StatusCode}.");
            _tl.T.TrackEvent("RLSDS.RlsDownloadFailed", ("StatusCode", response.StatusCode.ToString()));
            throw new Exception($"RLSDS.RlsDownloadFailed. {response.StatusCode}.");
        }

        return response;
    }

    private async Task CommitAsync((Uri Uri, string Version)? latestRel)
    {
        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
        if (!_regSettings.GetPackageRegistryRoot(out var regRoot))
        {
            _tl.L.WriteError($"GetPackageRegistryRoot failed.");
            throw new Exception($"GetPackageRegistryRoot failed.");
        }

        Registry.SetValue(regRoot, InstalledRlsVersionKey, latestRel?.Version);
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
