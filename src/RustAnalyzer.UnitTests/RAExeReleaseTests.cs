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
        var ret = await RAInstallerService.GetLatestRAReleaseRedirectUriAsync();

        var latestRelDate = DateTime.ParseExact(ret?.Version, RAInstallerService.RAVersionFormat, CultureInfo.InvariantCulture);
        var lastUpdateDate = DateTime.ParseExact(RAInstallerService.LatestInPackageRAVersion, RAInstallerService.RAVersionFormat, CultureInfo.InvariantCulture);
        lastUpdateDate.Should().NotBeBefore(latestRelDate.AddDays(-120), $"new rust-analyzer.exe is available {ret?.Uri}");
    }
}
