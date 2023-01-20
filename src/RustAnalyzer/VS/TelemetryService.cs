using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using KS.RustAnalyzer.Common;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace KS.RustAnalyzer.VS;

[Export(typeof(ITelemetryService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TelemetryService : ITelemetryService
{
    private readonly TelemetryClient _telemetryClient;

    public TelemetryService()
    {
        var configuration = TelemetryConfiguration.CreateDefault();
        configuration.TelemetryInitializers.Add(new DefaultPropertiesTelemetryInitializer());
        SetConnectionString(configuration);
        _telemetryClient = new TelemetryClient(configuration);
    }

    public void TrackEvent(string eventName, params (string key, string value)[] properties)
    {
        var propsDict = CreatePropsDict(properties);
        _telemetryClient.TrackEvent(eventName, propsDict);
    }

    public void TrackException(Exception e, [CallerMemberName] string siteName = null)
    {
        var propsDict = CreatePropsDict(new[] { ("Site", siteName) });
        _telemetryClient.TrackException(e, propsDict);
    }

    public void TrackException(Exception e, (string key, string value)[] properties, [CallerMemberName] string siteName = null)
    {
        var propsDict = CreatePropsDict(properties.Concat(new[] { ("Site", siteName) }).ToArray());
        _telemetryClient.TrackException(e, propsDict);
    }

    private static Dictionary<string, string> CreatePropsDict((string key, string value)[] properties)
    {
        return properties.Aggregate(
            new Dictionary<string, string>(),
            (acc, e) =>
            {
                acc.Add(e.key, e.value);
                return acc;
            });
    }

    private void SetConnectionString(TelemetryConfiguration configuration)
    {
        if (IsExperimentalInstance())
        {
            return;
        }

        configuration.ConnectionString = "InstrumentationKey=e2b04606-c5f2-41e5-b906-c6310a0c1900;IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus.livediagnostics.monitor.azure.com/";
    }

    private bool IsExperimentalInstance()
    {
        var env = Process.GetCurrentProcess().StartInfo.Environment;
        if (env.TryGetValue("VSROOTSUFFIX", out string rootSuffix) && rootSuffix.Equals("exp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public class DefaultPropertiesTelemetryInitializer : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry is ISupportProperties telemetryWithProperties)
            {
                telemetryWithProperties.Properties["VsixVersion"] = Vsix.Version;
            }
        }
    }
}
