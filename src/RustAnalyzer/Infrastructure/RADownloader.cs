using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

public static class RADownloader
{
    public const string RAVersionFormat = "yyyy-MM-dd";

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
