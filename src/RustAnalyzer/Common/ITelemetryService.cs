namespace KS.RustAnalyzer.Common;

public interface ITelemetryService
{
    void TrackEvent(string eventName, params (string key, string value)[] properties);
}
