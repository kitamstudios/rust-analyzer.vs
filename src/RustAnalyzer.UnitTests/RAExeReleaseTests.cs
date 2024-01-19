using System;
using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Xunit;

namespace KS.RustAnalyzer.UnitTests;

public sealed class RAExeReleaseTests
{
    [Fact]
    public async Task LastUpdateShouldNotBeOlderThan30DaysAsync()
    {
        var ret = await RADownloaderService.GetLatestRAReleaseRedirectUriAsync();

        var latestRelDate = DateTime.ParseExact(ret?.Version, RADownloaderService.RAVersionFormat, CultureInfo.InvariantCulture);
        var lastUpdateDate = DateTime.ParseExact(RADownloaderService.LatestInPackageRAVersion, RADownloaderService.RAVersionFormat, CultureInfo.InvariantCulture);
        lastUpdateDate.Should().NotBeBefore(latestRelDate.AddDays(-30), $"new rust-analyzer.exe is available https://github.com/rust-lang/rust-analyzer/releases/download/{ret?.Version}/rust-analyzer-x86_64-pc-windows-msvc.zip");
    }
}
