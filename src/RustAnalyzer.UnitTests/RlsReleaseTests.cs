using System;
using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using Xunit;

namespace KS.RustAnalyzer.UnitTests;

public sealed class RlsReleaseTests
{
    [Fact]
    public async Task LastUpdateShouldNotBeOlderThan30DaysAsync()
    {
        var ret = await RlsInstallerService.GetLatestRlsReleaseRedirectUriAsync();

        var latestRelDate = DateTime.ParseExact(ret?.Version, RlsInstallerService.VersionFormat, CultureInfo.InvariantCulture);
        var lastUpdateDate = DateTime.ParseExact(Constants.RlsLatestInPackageVersion, RlsInstallerService.VersionFormat, CultureInfo.InvariantCulture);
        lastUpdateDate.Should().NotBeBefore(latestRelDate.AddDays(-120), $"new rust-analyzer.exe is available {ret?.Uri}");
    }
}
