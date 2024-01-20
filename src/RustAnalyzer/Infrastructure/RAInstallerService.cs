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
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;

namespace KS.RustAnalyzer.Infrastructure;

public interface IRAInstallerService
{
    Task<PathEx> GetRustAnalyzerExePathAsync();

    Task InstallLatestRAAsync();
}

[Export(typeof(IRAInstallerService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class RAInstallerService : IRAInstallerService
{
    public const string LatestInPackageRAVersion = "2024-01-08";
    public const string RAVersionFormat = "yyyy-MM-dd";
    private const string InstalledRAVersionKey = "InstalledRAVersion";
    private readonly IRegistrySettingsService _regSettings;
    private readonly TL _tl;

    [ImportingConstructor]
    public RAInstallerService(IRegistrySettingsService regSettings, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _regSettings = regSettings;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public async Task InstallLatestRAAsync()
    {
        _tl.L.WriteLine("Initiating download of RA...");
        try
        {
            var latestRel = await GetLatestRAReleaseRedirectUriAsync();
            string installedVer = await GetInstalledVersionAsync();
            if (latestRel != null && installedVer.CompareTo(latestRel?.Version) >= 0)
            {
                _tl.L.WriteLine($"Not going to download RA. Installed = {installedVer}, Latest = {latestRel?.Uri}.");
                _tl.T.TrackEvent("RADS.RAUpToDate", ("Installed", installedVer), ("Latest", latestRel?.Uri.ToString()));
                return;
            }

            using var response = await DownloadAsync(latestRel);

            using var zipStream = await response.Content.ReadAsStreamAsync();
            Install(zipStream, latestRel?.Version);

            await CommitAsync(latestRel);
            _tl.T.TrackEvent("RADS.RAInstalled", ("Installed", installedVer));
        }
        catch (Exception ex)
        {
            _tl.L.WriteError($"Download failed. StatusCode {ex}");
            _tl.T.TrackException(ex);
            throw;
        }
    }

    public async Task<PathEx> GetRustAnalyzerExePathAsync()
    {
        return GetVersionedRAExePath(await GetInstalledVersionAsync());
    }

    public static async Task<(Uri Uri, string Version)?> GetLatestRAReleaseRedirectUriAsync()
    {
        try
        {
            var latestRelUri = await GetRedirectedUrlAsync("https://github.com/rust-lang/rust-analyzer/releases/latest".ToUri());

            var latestRelVersion = latestRelUri.Segments[latestRelUri.Segments.Length - 1];
            var latestRelDate = DateTime.ParseExact(latestRelVersion, RAVersionFormat, CultureInfo.InvariantCulture);

            return (Uri: new Uri($"https://github.com/rust-lang/rust-analyzer/releases/download/{latestRelVersion}/rust-analyzer-x86_64-pc-windows-msvc.zip"),
                Version: latestRelDate.ToString(RAVersionFormat, CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private PathEx GetVersionedRAExePath(string version)
    {
        return GetRAFolder(version) + (PathEx)$"rust-analyzer.exe";
    }

    private async Task<HttpResponseMessage> DownloadAsync((Uri Uri, string Version)? latestRel)
    {
        _tl.L.WriteLine($"Downloading RA v{latestRel?.Uri}.");
        var response = await new HttpClient().GetAsync(latestRel?.Uri);
        if (!response.IsSuccessStatusCode)
        {
            _tl.L.WriteError($"Download failed. StatusCode {response.StatusCode}.");
            _tl.T.TrackEvent("RADS.RADownloadFailed", ("StatusCode", response.StatusCode.ToString()));
            throw new Exception($"RADS.RADownloadFailed. {response.StatusCode}.");
        }

        return response;
    }

    private async Task CommitAsync((Uri Uri, string Version)? latestRel)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (!_regSettings.GetPackageRegistryRoot(out var regRoot))
        {
            _tl.L.WriteError($"GetPackageRegistryRoot failed.");
            throw new Exception($"GetPackageRegistryRoot failed.");
        }

        Registry.SetValue(regRoot, InstalledRAVersionKey, latestRel?.Version);
        _tl.L.WriteLine($"Committed RA installation.");
    }

    private void Install(Stream zipStream, string downloadedVersion)
    {
        _tl.L.WriteLine($"Installing RA v{downloadedVersion}...");
        var raFolder = GetRAFolder(downloadedVersion);
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
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var installedRAVersion = LatestInPackageRAVersion;
        if (_regSettings.GetPackageRegistryRoot(out var regRoot))
        {
            installedRAVersion = Registry.GetValue(regRoot, InstalledRAVersionKey, null) as string;
        }

        installedRAVersion ??= LatestInPackageRAVersion;
        if (GetVersionedRAExePath(installedRAVersion).FileExists())
        {
            return installedRAVersion;
        }

        return LatestInPackageRAVersion;
    }

    private static PathEx GetRAFolder(string version)
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
