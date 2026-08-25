using System;
using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using Xunit;

namespace KS.RustAnalyzer.UnitTests;

[Trait("type", "IntegrationTests")]
public sealed class RlsReleaseTests
{
    [Fact]
    [Trait("scope", "External")]
    public async Task LastUpdateShouldNotBeOlderThan30DaysAsync()
    {
        var ret = await RlsInstallerService.GetLatestRlsReleaseRedirectUriAsync();

        var latestRelDate = DateTime.ParseExact(ret?.Version, RlsInstallerService.VersionFormat, CultureInfo.InvariantCulture);
        var lastUpdateDate = DateTime.ParseExact(Constants.RlsLatestInPackageVersion, RlsInstallerService.VersionFormat, CultureInfo.InvariantCulture);
        lastUpdateDate.Should().NotBeBefore(latestRelDate.AddDays(-30), $"new rust-analyzer.exe is available {ret?.Uri}");
    }
}
