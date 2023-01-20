using System;
using System.Runtime.CompilerServices;

namespace KS.RustAnalyzer.Common;

public interface ITelemetryService
{
    void TrackEvent(string eventName, params (string key, string value)[] properties);

    void TrackException(Exception e, [CallerMemberName] string siteName = null);

    void TrackException(Exception e, (string key, string value)[] properties, [CallerMemberName] string siteName = null);
}
