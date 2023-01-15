namespace KS.RustAnalyzer.Common;

public interface ITelemetryService
{
    void TrackEvent(string eventName, (string key, string value)[] properties);
}
