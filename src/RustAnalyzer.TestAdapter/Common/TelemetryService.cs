using System;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface ITelemetryService
{
    void TrackEvent(string eventName, params (string Key, string Value)[] properties);

    void TrackException(Exception e, [CallerMemberName] string siteName = null);

    void TrackException(Exception e, (string Key, string Value)[] properties, [CallerMemberName] string siteName = null);
}

[Export(typeof(ITelemetryService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TelemetryService : ITelemetryService
{
    public void TrackEvent(string eventName, params (string Key, string Value)[] properties)
    {
    }

    public void TrackException(Exception e, [CallerMemberName] string siteName = null)
    {
    }

    public void TrackException(Exception e, (string Key, string Value)[] properties, [CallerMemberName] string siteName = null)
    {
    }
}
