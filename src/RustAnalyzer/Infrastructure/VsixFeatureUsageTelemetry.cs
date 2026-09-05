using System;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

[Export(typeof(IFeatureUsageTelemetry))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class VsixFeatureUsageTelemetry : IFeatureUsageTelemetry
{
    private readonly IFeatureUsageTelemetry _telemetry = FeatureUsageTelemetry.CreateForVsix();

    public void Track(UsageOperation operation, UsageOutcome outcome, TimeSpan duration)
    {
        _telemetry.Track(operation, outcome, duration);
    }
}
