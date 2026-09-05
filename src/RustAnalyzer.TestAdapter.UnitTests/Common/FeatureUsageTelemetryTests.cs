using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

[Trait("type", "UnitTests")]
public sealed class FeatureUsageTelemetryTests
{
    private const string ExpectedUserId = "ravs-v1:rxiD3uWNIBWVabLFd8fp3XV7s8FACaMtgtRMWTIZE4c";

    [Fact]
    public void TypedContractHasOnlyClosedInputs()
    {
        var method = typeof(IFeatureUsageTelemetry).GetMethods().Should().ContainSingle().Subject;

        method.Name.Should().Be("Track");
        method.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(UsageOperation), typeof(UsageOutcome), typeof(TimeSpan));
    }

    [Fact]
    public void MapsEveryOperation()
    {
        var expected = new[]
        {
            (UsageOperation.LanguageServerActivate, "language_server", "activate"),
            (UsageOperation.CargoBuild, "cargo", "build"),
            (UsageOperation.CargoClean, "cargo", "clean"),
            (UsageOperation.CargoClippy, "cargo", "clippy"),
            (UsageOperation.CargoFormat, "cargo", "format"),
            (UsageOperation.TestAdapterDiscover, "test_adapter", "discover"),
            (UsageOperation.TestAdapterExecute, "test_adapter", "execute"),
            (UsageOperation.LaunchDebug, "launch", "debug"),
            (UsageOperation.LaunchRun, "launch", "run"),
            (UsageOperation.ToolchainInstall, "toolchain", "install"),
            (UsageOperation.ToolchainSwitch, "toolchain", "switch"),
        };
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = CreateConfigured(channel);

        Enum.GetValues(typeof(UsageOperation)).Cast<UsageOperation>()
            .Should().Equal(expected.Select(value => value.Item1));

        foreach (var (operation, feature, action) in expected)
        {
            telemetry.Track(operation, UsageOutcome.Succeeded, TimeSpan.Zero);
            var item = channel.Items.Last().Should().BeOfType<EventTelemetry>().Subject;
            item.Properties["feature"].Should().Be(feature);
            item.Properties["action"].Should().Be(action);
        }
    }

    [Fact]
    public void MapsEveryOutcome()
    {
        var expected = new[]
        {
            (UsageOutcome.Succeeded, "succeeded"),
            (UsageOutcome.Failed, "failed"),
            (UsageOutcome.Cancelled, "cancelled"),
        };
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = CreateConfigured(channel);

        Enum.GetValues(typeof(UsageOutcome)).Cast<UsageOutcome>()
            .Should().Equal(expected.Select(value => value.Item1));

        foreach (var (outcome, wireValue) in expected)
        {
            telemetry.Track(UsageOperation.CargoBuild, outcome, TimeSpan.Zero);
            var item = channel.Items.Last().Should().BeOfType<EventTelemetry>().Subject;
            item.Properties["outcome"].Should().Be(wireValue);
        }
    }

    [Theory]
    [InlineData(0L, "<1s")]
    [InlineData(TimeSpan.TicksPerSecond - 1, "<1s")]
    [InlineData(TimeSpan.TicksPerSecond, "1–5s")]
    [InlineData((5 * TimeSpan.TicksPerSecond) - 1, "1–5s")]
    [InlineData(5 * TimeSpan.TicksPerSecond, "5–30s")]
    [InlineData((30 * TimeSpan.TicksPerSecond) - 1, "5–30s")]
    [InlineData(30 * TimeSpan.TicksPerSecond, "30–120s")]
    [InlineData((120 * TimeSpan.TicksPerSecond) - 1, "30–120s")]
    [InlineData(120 * TimeSpan.TicksPerSecond, "≥120s")]
    public void MapsEveryDurationBoundary(long ticks, string expected)
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = CreateConfigured(channel);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.FromTicks(ticks));

        channel.Items.Should().ContainSingle();
        ((EventTelemetry)channel.Items[0]).Properties["duration_bucket"].Should().Be(expected);
    }

    [Fact]
    public void HashesTheExactExpandedIdentity()
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = CreateConfigured(channel);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.Zero);

        ((EventTelemetry)channel.Items.Single()).Context.User.Id.Should().Be(ExpectedUserId);
    }

    [Theory]
    [InlineData("USERNAME")]
    [InlineData("COMPUTERNAME")]
    [InlineData("USERDOMAIN")]
    public void OmitsIdentityWhenAPlaceholderIsUnresolved(string unresolvedVariable)
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment((unresolvedVariable, null));
        var telemetry = CreateConfigured(channel);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.Zero);

        ((EventTelemetry)channel.Items.Single()).Context.User.Id.Should().BeNull();
    }

    [Theory]
    [InlineData(UsageHostKind.Vsix, "vsix", "17.12.1", "17")]
    [InlineData(UsageHostKind.TestAdapter, "test_adapter", "18.0", "18")]
    [InlineData(UsageHostKind.Vsix, "vsix", "16.11", "unknown")]
    [InlineData(UsageHostKind.TestAdapter, "test_adapter", null, "unknown")]
    public void EmitsOnlyAllowedSchemaContext(
        UsageHostKind hostKind,
        string expectedHost,
        string visualStudioVersion,
        string expectedMajor)
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment(("RAVsVersion", visualStudioVersion));
        var telemetry = CreateConfigured(channel, hostKind);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.Zero);

        var item = (EventTelemetry)channel.Items.Single();
        item.Name.Should().Be(FeatureUsageTelemetry.EventName);
        item.Properties.Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["feature"] = "cargo",
                ["action"] = "build",
                ["outcome"] = "succeeded",
                ["duration_bucket"] = "<1s",
            });
        item.Context.GlobalProperties.Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["schema_version"] = "1",
                ["host_kind"] = expectedHost,
                ["extension_version"] = GetExtensionVersion(),
                ["visual_studio_major"] = expectedMajor,
            });
        item.Context.User.Id.Should().Be(ExpectedUserId);
    }

    [Fact]
#pragma warning disable CS0618
    public void RemovesForbiddenPropertiesAndSdkContext()
    {
        var next = new RecordingTelemetryProcessor();
        var processor = new FeatureUsageTelemetry.AllowListTelemetryProcessor(next, UsageHostKind.Vsix);
        var item = CreateAllowedEvent();
        item.Properties.Add("path", @"C:\secret");
        item.Metrics.Add("measurement", 42);
        item.Sequence = "sequence";
        item.Context.Properties.Add("sdk_property", "value");
        item.Context.GlobalProperties.Add("account", "value");
        item.Context.InstrumentationKey = "transport-key";
        item.Context.Flags = 7;
        item.Context.Component.Version = "sdk-version";
        item.Context.Device.Id = "device";
        item.Context.Device.OperatingSystem = "operating-system";
        item.Context.Cloud.RoleName = "role";
        item.Context.Session.Id = "session";
        item.Context.Session.IsFirst = true;
        item.Context.User.Id = "raw-user";
        item.Context.User.AccountId = "account";
        item.Context.User.AuthenticatedUserId = "authenticated-user";
        item.Context.User.UserAgent = "user-agent";
        item.Context.Operation.Id = "operation";
        item.Context.Location.Ip = "location";
        using var environment = StandardEnvironment();

        processor.Process(item);

        var forwarded = (EventTelemetry)next.Items.Should().ContainSingle().Subject;
        forwarded.Properties.Keys.Should().BeEquivalentTo("feature", "action", "outcome", "duration_bucket");
        forwarded.Metrics.Should().BeEmpty();
        forwarded.Extension.Should().BeNull();
        forwarded.Sequence.Should().BeNull();
        forwarded.Context.Properties.Keys.Should().BeEquivalentTo(
            "feature",
            "action",
            "outcome",
            "duration_bucket");
        forwarded.Context.GlobalProperties.Keys.Should().BeEquivalentTo(
            "schema_version",
            "host_kind",
            "extension_version",
            "visual_studio_major");
        forwarded.Context.Flags.Should().Be(0);
        forwarded.Context.InstrumentationKey.Should().Be("transport-key");
        forwarded.Context.Component.Version.Should().BeNull();
        forwarded.Context.Device.Id.Should().BeNull();
        forwarded.Context.Device.OperatingSystem.Should().BeNull();
        forwarded.Context.Cloud.RoleName.Should().BeNull();
        forwarded.Context.Session.Id.Should().BeNull();
        forwarded.Context.Session.IsFirst.Should().BeNull();
        forwarded.Context.User.Id.Should().Be(ExpectedUserId);
        forwarded.Context.User.AccountId.Should().BeNull();
        forwarded.Context.User.AuthenticatedUserId.Should().BeNull();
        forwarded.Context.User.UserAgent.Should().BeNull();
        forwarded.Context.Operation.Id.Should().BeNull();
        forwarded.Context.Location.Ip.Should().BeNull();
    }
#pragma warning restore CS0618

    [Fact]
    public void RejectsNonContractTelemetryAndValues()
    {
        var next = new RecordingTelemetryProcessor();
        var processor = new FeatureUsageTelemetry.AllowListTelemetryProcessor(next, UsageHostKind.TestAdapter);
        processor.Process(new ExceptionTelemetry(new InvalidOperationException()));
        processor.Process(new EventTelemetry("other"));
        processor.Process(CreateAllowedEvent(("feature", "other")));
        processor.Process(CreateAllowedEvent(("action", "other")));
        processor.Process(CreateAllowedEvent(("outcome", "other")));
        processor.Process(CreateAllowedEvent(("duration_bucket", "other")));
        var missingProperty = CreateAllowedEvent();
        missingProperty.Properties.Remove("feature");
        processor.Process(missingProperty);

        next.Items.Should().BeEmpty();
    }

    [Fact]
    public void ConfiguredEnabledTelemetryEmits()
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = CreateConfigured(channel);

        telemetry.Track(UsageOperation.LanguageServerActivate, UsageOutcome.Succeeded, TimeSpan.Zero);

        channel.Items.Should().ContainSingle()
            .Which.Should().BeOfType<EventTelemetry>()
            .Which.Name.Should().Be(FeatureUsageTelemetry.EventName);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("false")]
    [InlineData(" ")]
    public void DisabledTelemetryUsesNoOpWithoutAnActiveClient(string disabled)
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment(("RUSTANALYZER_TELEMETRY_DISABLED", disabled));
        var telemetry = Create(UsageHostKind.Vsix, CreateConnectionString(), channel);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.Zero);

        channel.Items.Should().BeEmpty();
        GetInstanceFields(telemetry).Should().BeEmpty();
    }

    [Fact]
    public void ExperimentalInstanceUsesNoOpWithoutAnActiveClient()
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment(("VSROOTSUFFIX", "Exp"));
        var telemetry = Create(UsageHostKind.Vsix, CreateConnectionString(), channel);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.Zero);

        channel.Items.Should().BeEmpty();
        GetInstanceFields(telemetry).Should().BeEmpty();
    }

    [Fact]
    public void MissingConfigurationUsesNoOpWithoutAnActiveClient()
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = Create(UsageHostKind.TestAdapter, null, channel);

        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.Zero);

        channel.Items.Should().BeEmpty();
        GetInstanceFields(telemetry).Should().BeEmpty();
    }

    [Fact]
    public void InvalidTypedValuesFailClosed()
    {
        var channel = new RecordingTelemetryChannel();
        using var environment = StandardEnvironment();
        var telemetry = CreateConfigured(channel);

        telemetry.Track((UsageOperation)(-1), UsageOutcome.Succeeded, TimeSpan.Zero);
        telemetry.Track(UsageOperation.CargoBuild, (UsageOutcome)(-1), TimeSpan.Zero);
        telemetry.Track(UsageOperation.CargoBuild, UsageOutcome.Succeeded, TimeSpan.FromTicks(-1));

        channel.Items.Should().BeEmpty();
    }

    [Fact]
    public void LegacyTelemetryHasNoEgress()
    {
        var telemetry = new TelemetryService();

        telemetry.TrackEvent("event", ("path", @"C:\secret"));
        telemetry.TrackException(new InvalidOperationException("secret"));
        telemetry.TrackException(
            new InvalidOperationException("secret"),
            new[] { ("path", @"C:\secret") });

        GetInstanceFields(telemetry).Should().BeEmpty();
    }

    private static IFeatureUsageTelemetry CreateConfigured(
        RecordingTelemetryChannel channel,
        UsageHostKind hostKind = UsageHostKind.TestAdapter)
    {
        return Create(hostKind, CreateConnectionString(), channel);
    }

    private static IFeatureUsageTelemetry Create(
        UsageHostKind hostKind,
        string connectionString,
        ITelemetryChannel channel)
    {
        var create = typeof(FeatureUsageTelemetry).GetMethod(
            "Create",
            BindingFlags.NonPublic | BindingFlags.Static);
        create.Should().NotBeNull();
        return (IFeatureUsageTelemetry)create.Invoke(
            null,
            new object[] { hostKind, connectionString, channel });
    }

    private static EventTelemetry CreateAllowedEvent(params (string Key, string Value)[] overrides)
    {
        var item = new EventTelemetry(FeatureUsageTelemetry.EventName);
        item.Properties.Add("feature", "cargo");
        item.Properties.Add("action", "build");
        item.Properties.Add("outcome", "succeeded");
        item.Properties.Add("duration_bucket", "<1s");
        foreach (var (key, value) in overrides)
        {
            item.Properties[key] = value;
        }

        return item;
    }

    private static string CreateConnectionString()
    {
        return "Instrumentation" + "Key=" + Guid.NewGuid().ToString("D");
    }

    private static string GetExtensionVersion()
    {
        var vsixType = typeof(Constants).Assembly.GetType("KS.RustAnalyzer.Vsix");
        return (string)vsixType.GetField("Version", BindingFlags.Public | BindingFlags.Static)
            .GetRawConstantValue();
    }

    private static FieldInfo[] GetInstanceFields(object value)
    {
        return value.GetType().GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static EnvironmentScope StandardEnvironment(
        params (string Name, string Value)[] overrides)
    {
        var values = new List<(string Name, string Value)>
        {
            ("VSROOTSUFFIX", null),
            ("RUSTANALYZER_TELEMETRY_DISABLED", null),
            ("USERNAME", "Alice "),
            ("COMPUTERNAME", "BuildHost"),
            ("USERDOMAIN", "Contoso"),
            ("RAVsVersion", "17.12"),
        };
        values.AddRange(overrides);
        return new EnvironmentScope(values.ToArray());
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string> _originalValues = new();

        public EnvironmentScope(params (string Name, string Value)[] values)
        {
            foreach (var (name, value) in values)
            {
                if (!_originalValues.ContainsKey(name))
                {
                    _originalValues.Add(name, Environment.GetEnvironmentVariable(name));
                }

                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var value in _originalValues)
            {
                Environment.SetEnvironmentVariable(value.Key, value.Value);
            }
        }
    }

    private sealed class RecordingTelemetryChannel : ITelemetryChannel
    {
        public bool? DeveloperMode { get; set; }

        public string EndpointAddress { get; set; }

        public List<ITelemetry> Items { get; } = new();

        public void Send(ITelemetry item)
        {
            Items.Add(item);
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingTelemetryProcessor : ITelemetryProcessor
    {
        public List<ITelemetry> Items { get; } = new();

        public void Process(ITelemetry item)
        {
            Items.Add(item);
        }
    }
}
