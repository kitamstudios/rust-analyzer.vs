using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

public interface IRADownloaderService
{
    Task<PathEx> GetRustAnalyzerExePathAsync();

    Task DownloadLatestRAAsync();
}

[Export(typeof(IRADownloaderService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class RADownloaderService : IRADownloaderService
{
    public const string LatestInPackageRAVersion = "2024-01-08";

    public const string RAVersionFormat = "yyyy-MM-dd";
    private readonly IRegistrySettingsService _regSettings;
    private readonly TL _tl;

    [ImportingConstructor]
    public RADownloaderService(IRegistrySettingsService regSettings, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _regSettings = regSettings;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public Task DownloadLatestRAAsync()
    {
        _tl.L.WriteLine("Initiating download of RA...");
        return Task.CompletedTask;
    }

    public Task<PathEx> GetRustAnalyzerExePathAsync()
    {
        var path = (PathEx)Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), $"rust-analyzer.{LatestInPackageRAVersion}.exe");
        return path.ToTask();
    }

    public static async Task<(Uri Uri, string Version)?> GetLatestRAReleaseRedirectUriAsync()
    {
        try
        {
            var latestRelUri = await GetRedirectedUrlAsync("https://github.com/rust-lang/rust-analyzer/releases/latest".ToUri());

            var latestRelVersion = latestRelUri.Segments[latestRelUri.Segments.Length - 1];
            var latestRelDate = DateTime.ParseExact(latestRelVersion, RAVersionFormat, CultureInfo.InvariantCulture);

            return (Uri: latestRelUri, Version: latestRelDate.ToString(RAVersionFormat, CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Uri> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, }, true);
        using var response = await client.GetAsync(uri, cancellationToken);

        return new Uri(response.Headers.GetValues("Location").First());
    }
}
