using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EnsureThat;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IFeatureUsageTelemetry
{
    void Track(UsageOperation operation, UsageOutcome outcome, TimeSpan duration);
}

public enum UsageOperation
{
    LanguageServerActivate,
    CargoBuild,
    CargoClean,
    CargoClippy,
    CargoFormat,
    TestAdapterDiscover,
    TestAdapterExecute,
    LaunchDebug,
    LaunchRun,
    ToolchainInstall,
    ToolchainSwitch,
}

public enum UsageOutcome
{
    Succeeded,
    Failed,
    Cancelled,
}

public enum UsageHostKind
{
    Vsix,
    TestAdapter,
}

public static class FeatureUsageTelemetry
{
    public const string EventName = "rustanalyzer.feature_usage";

    private const string ConfigurationResourceName = "KS.RustAnalyzer.TelemetryConfiguration";
    private const string ConfigurationResourcePrefix = "ravs-v1:";
    private const string IdentityTemplate = "%USERNAME%@%COMPUTERNAME%.%USERDOMAIN%";

    private static readonly IReadOnlyDictionary<UsageOperation, (string Feature, string Action)> Operations =
        new Dictionary<UsageOperation, (string Feature, string Action)>
        {
            [UsageOperation.LanguageServerActivate] = ("language_server", "activate"),
            [UsageOperation.CargoBuild] = ("cargo", "build"),
            [UsageOperation.CargoClean] = ("cargo", "clean"),
            [UsageOperation.CargoClippy] = ("cargo", "clippy"),
            [UsageOperation.CargoFormat] = ("cargo", "format"),
            [UsageOperation.TestAdapterDiscover] = ("test_adapter", "discover"),
            [UsageOperation.TestAdapterExecute] = ("test_adapter", "execute"),
            [UsageOperation.LaunchDebug] = ("launch", "debug"),
            [UsageOperation.LaunchRun] = ("launch", "run"),
            [UsageOperation.ToolchainInstall] = ("toolchain", "install"),
            [UsageOperation.ToolchainSwitch] = ("toolchain", "switch"),
        };

    private static readonly IReadOnlyDictionary<UsageOutcome, string> Outcomes =
        new Dictionary<UsageOutcome, string>
        {
            [UsageOutcome.Succeeded] = "succeeded",
            [UsageOutcome.Failed] = "failed",
            [UsageOutcome.Cancelled] = "cancelled",
        };

    private static readonly string[] DurationBuckets =
    {
        "<1s",
        "1–5s",
        "5–30s",
        "30–120s",
        "≥120s",
    };

    private static readonly IFeatureUsageTelemetry NoOp = new NoOpFeatureUsageTelemetry();

    public static IFeatureUsageTelemetry CreateForVsix()
    {
        return Create(UsageHostKind.Vsix, GetConnectionString(), null);
    }

    public static IFeatureUsageTelemetry CreateForTestAdapter()
    {
        return Create(UsageHostKind.TestAdapter, GetConnectionString(), null);
    }

    private static IFeatureUsageTelemetry Create(
        UsageHostKind hostKind,
        string connectionString,
        ITelemetryChannel channel)
    {
        if (IsTelemetryDisabled() || IsExperimentalInstance() || string.IsNullOrEmpty(connectionString))
        {
            return NoOp;
        }

        var configuration = TelemetryConfiguration.CreateDefault();
        configuration.TelemetryInitializers.Clear();
        configuration.ConnectionString = connectionString;
        if (channel != null)
        {
            configuration.TelemetryChannel = channel;
        }

        var builder = configuration.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
        builder.Use(next => new AllowListTelemetryProcessor(next, hostKind));
        builder.Build();

        return new ApplicationInsightsFeatureUsageTelemetry(new TelemetryClient(configuration));
    }

    private static string GetConnectionString()
    {
        using var stream = typeof(FeatureUsageTelemetry).Assembly
            .GetManifestResourceStream(ConfigurationResourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var configuration = reader.ReadLine();
        return configuration?.Length > ConfigurationResourcePrefix.Length
            ? configuration.Substring(ConfigurationResourcePrefix.Length)
            : null;
    }

    private static bool IsExperimentalInstance()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("VSROOTSUFFIX"),
            "exp",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTelemetryDisabled()
    {
        return !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("RUSTANALYZER_TELEMETRY_DISABLED"));
    }

    private static bool TryGetDurationBucket(TimeSpan duration, out string value)
    {
        if (duration < TimeSpan.Zero)
        {
            value = null;
            return false;
        }

        if (duration < TimeSpan.FromSeconds(1))
        {
            value = "<1s";
        }
        else if (duration < TimeSpan.FromSeconds(5))
        {
            value = "1–5s";
        }
        else if (duration < TimeSpan.FromSeconds(30))
        {
            value = "5–30s";
        }
        else if (duration < TimeSpan.FromSeconds(120))
        {
            value = "30–120s";
        }
        else
        {
            value = "≥120s";
        }

        return true;
    }

    public sealed class AllowListTelemetryProcessor : ITelemetryProcessor
    {
        private readonly string _hostKind;
        private readonly ITelemetryProcessor _next;

        public AllowListTelemetryProcessor(ITelemetryProcessor next, UsageHostKind hostKind)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsTrue(
                hostKind == UsageHostKind.Vsix || hostKind == UsageHostKind.TestAdapter,
                nameof(hostKind));
            _hostKind = hostKind == UsageHostKind.Vsix ? "vsix" : "test_adapter";
        }

        public void Process(ITelemetry item)
        {
            if (!(item is EventTelemetry telemetry)
                || !string.Equals(telemetry.Name, EventName, StringComparison.Ordinal)
                || !HasAllowedValues(telemetry))
            {
                return;
            }

            _next.Process(CreateAllowedTelemetry(telemetry));
        }

        private static bool HasAllowedValues(EventTelemetry telemetry)
        {
            if (!telemetry.Properties.TryGetValue("feature", out var feature)
                || !telemetry.Properties.TryGetValue("action", out var action)
                || !telemetry.Properties.TryGetValue("outcome", out var outcome)
                || !telemetry.Properties.TryGetValue("duration_bucket", out var durationBucket))
            {
                return false;
            }

            return Operations.Values.Any(operation => operation.Feature == feature && operation.Action == action)
                && Outcomes.Values.Contains(outcome)
                && DurationBuckets.Contains(durationBucket);
        }

        private EventTelemetry CreateAllowedTelemetry(EventTelemetry source)
        {
            var telemetry = new EventTelemetry(EventName)
            {
                Timestamp = source.Timestamp,
            };
            telemetry.Properties.Add("feature", source.Properties["feature"]);
            telemetry.Properties.Add("action", source.Properties["action"]);
            telemetry.Properties.Add("outcome", source.Properties["outcome"]);
            telemetry.Properties.Add("duration_bucket", source.Properties["duration_bucket"]);

            var context = telemetry.Context;
            context.GlobalProperties.Add("schema_version", "1");
            context.GlobalProperties.Add("host_kind", _hostKind);
            context.GlobalProperties.Add("extension_version", Vsix.Version);
            context.GlobalProperties.Add("visual_studio_major", GetVisualStudioMajor());
            context.User.Id = CreateUserId();
            context.InstrumentationKey = source.Context.InstrumentationKey;
            return telemetry;
        }

        private static string GetVisualStudioMajor()
        {
            return Version.TryParse(
                    Environment.GetEnvironmentVariable(Constants.RAVsVersion),
                    out var version)
                && (version.Major == 17 || version.Major == 18)
                    ? version.Major.ToString()
                    : "unknown";
        }

        private static string CreateUserId()
        {
            var identity = Environment.ExpandEnvironmentVariables(IdentityTemplate);
            if (identity.IndexOf("%USERNAME%", StringComparison.Ordinal) >= 0
                || identity.IndexOf("%COMPUTERNAME%", StringComparison.Ordinal) >= 0
                || identity.IndexOf("%USERDOMAIN%", StringComparison.Ordinal) >= 0)
            {
                return null;
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
            var encodedHash = Convert.ToBase64String(hash)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return $"ravs-v1:{encodedHash}";
        }
    }

    private sealed class ApplicationInsightsFeatureUsageTelemetry : IFeatureUsageTelemetry
    {
        private readonly TelemetryClient _telemetryClient;

        public ApplicationInsightsFeatureUsageTelemetry(TelemetryClient telemetryClient)
        {
            _telemetryClient = EnsureArg.IsNotNull(telemetryClient, nameof(telemetryClient));
        }

        public void Track(UsageOperation operation, UsageOutcome outcome, TimeSpan duration)
        {
            if (!Operations.TryGetValue(operation, out var operationValue)
                || !Outcomes.TryGetValue(outcome, out var outcomeValue)
                || !TryGetDurationBucket(duration, out var durationBucket))
            {
                return;
            }

            var telemetry = new EventTelemetry(EventName);
            telemetry.Properties.Add("feature", operationValue.Feature);
            telemetry.Properties.Add("action", operationValue.Action);
            telemetry.Properties.Add("outcome", outcomeValue);
            telemetry.Properties.Add("duration_bucket", durationBucket);

            _telemetryClient.TrackEvent(telemetry);
        }
    }

    private sealed class NoOpFeatureUsageTelemetry : IFeatureUsageTelemetry
    {
        public void Track(UsageOperation operation, UsageOutcome outcome, TimeSpan duration)
        {
        }
    }
}
