using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KS.RustAnalyzer.Common;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.VS;

[ExportWorkspaceServiceFactory(WorkspaceServiceFactoryOptions.CreateOnWorkspaceInitialize, typeof(ITelemetryService))]
public sealed class TelemetryServiceFactory : IWorkspaceServiceFactory
{
    public object CreateService(IWorkspace workspaceContext)
    {
        var isExpInstance = IsExperimentalInstance();
        return !isExpInstance ? new TelemetryService() : new NullTelemetryService();
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
}

public sealed class TelemetryService : ITelemetryService
{
    private readonly TelemetryClient _telemetryClient;

    public TelemetryService()
    {
        TelemetryConfiguration configuration = TelemetryConfiguration.CreateDefault();
        configuration.ConnectionString = "InstrumentationKey=e2b04606-c5f2-41e5-b906-c6310a0c1900;IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus.livediagnostics.monitor.azure.com/";
        _telemetryClient = new TelemetryClient(configuration);
    }

    public void TrackEvent(string eventName, params (string key, string value)[] properties)
    {
        var propsDict = properties.Aggregate(
            new Dictionary<string, string>(),
            (acc, e) =>
            {
                acc.Add(e.key, e.value);
                return acc;
            });
        _telemetryClient.TrackEvent(eventName, propsDict);
    }
}

public sealed class NullTelemetryService : ITelemetryService
{
    public void TrackEvent(string eventName, (string key, string value)[] properties)
    {
    }
}
