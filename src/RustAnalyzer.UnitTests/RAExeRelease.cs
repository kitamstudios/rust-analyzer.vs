using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.UnitTests;

public sealed class RAExeRelease
{
    private const string LastUpdatedRAExeVersion = "2023-02-13";

    [Fact]
    public async Task CheckForNewReleaseAsync()
    {
        var latestRelUri = await GetRedirectedUrlAsync("https://github.com/rust-lang/rust-analyzer/releases/latest".ToUri());

        string latestRelVersion = latestRelUri.Segments[latestRelUri.Segments.Length - 1];
        LastUpdatedRAExeVersion.Should().Be(latestRelVersion, $"new rust-analyzer.exe is available https://github.com/rust-lang/rust-analyzer/releases/download/{latestRelVersion}/rust-analyzer-x86_64-pc-windows-msvc.zip");
    }

    private static async Task<Uri> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, }, true);
        using var response = await client.GetAsync(uri, cancellationToken);

        return new Uri(response.Headers.GetValues("Location").First());
    }
}
