using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Xunit;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteEvaluatorTests
{
    [Theory]
    [InlineData("16.11", false)]
    [InlineData("17.11.9", false)]
    [InlineData("17.12", true)]
    [InlineData("17.14.8", true)]
    [InlineData("18.0", true)]
    [InlineData("18.4.1", true)]
    [InlineData("18.99.7", true)]
    [InlineData("19.0", false)]
    public void SupportsOnlyApprovedVisualStudioVersions(string value, bool expected)
    {
        PrerequisiteEvaluator.IsSupportedVisualStudioVersion(Version.Parse(value)).Should().Be(expected);
    }

    [Fact]
    public async Task ClassifiesUndetectableVisualStudioHostAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            VisualStudioVersion = null,
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.UnsupportedVisualStudioHost);
        failure.Message.Should().Contain("could not be detected").And.Contain("Repair").And.Contain("restart Visual Studio");
    }

    [Fact]
    public async Task ClassifiesUnsupportedVisualStudioHostAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            VisualStudioVersion = new Version(19, 0),
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.UnsupportedVisualStudioHost);
        failure.Message.Should().Contain("19.0").And.Contain("17.12").And.Contain("18.x").And.Contain("restart Visual Studio");
    }

    [Fact]
    public async Task ClassifiesRustupMissingFromProcessPathAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            RustupPath = null,
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.RustupNotFound);
        failure.Message.Should().Contain("rustup.exe").And.Contain("process PATH").And.Contain("Install").And.Contain("restart Visual Studio");
        probe.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ClassifiesRustupCommandFailureAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            RustupVersionResult = PrerequisiteCommandResult.Completed(1, string.Empty, "rustup failed"),
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.RustupNotOperational);
        failure.Message.Should().Contain("exit code 1").And.Contain("Repair rustup").And.Contain("restart Visual Studio");
        probe.Commands.Should().Equal($"{probe.RustupPath}|--version");
    }

    [Fact]
    public async Task ClassifiesRustupStartFailureAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            RustupVersionResult = PrerequisiteCommandResult.FailedToStart("Access is denied."),
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.RustupNotOperational);
        failure.Message.Should().Contain("start failed").And.Contain("Access is denied").And.Contain("Repair rustup");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifiesMissingDefaultToolchainAsync(bool commandSucceedsWithoutOutput)
    {
        var probe = new FakePrerequisiteProbe
        {
            RustupDefaultResult = commandSucceedsWithoutOutput
                ? PrerequisiteCommandResult.Completed(0, string.Empty, string.Empty)
                : PrerequisiteCommandResult.Completed(1, string.Empty, "no default toolchain configured"),
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.DefaultToolchainNotConfigured);
        failure.Message.Should().Contain("rustup default stable").And.Contain("restart Visual Studio");
        probe.Commands.Should().Equal(
            $"{probe.RustupPath}|--version",
            $"{probe.RustupPath}|default");
    }

    [Fact]
    public async Task ClassifiesCargoMissingFromProcessPathAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            CargoPath = null,
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.CargoNotFound);
        failure.Message.Should().Contain("cargo.exe").And.Contain("process PATH").And.Contain("Install").And.Contain("restart Visual Studio");
        probe.Commands.Should().Equal(
            $"{probe.RustupPath}|--version",
            $"{probe.RustupPath}|default");
    }

    [Fact]
    public async Task ClassifiesCargoCommandFailureAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            CargoVersionResult = PrerequisiteCommandResult.Completed(101, string.Empty, "cargo failed"),
        };

        var failure = await EvaluateSingleFailureAsync(probe);

        failure.Kind.Should().Be(PrerequisiteFailureKind.CargoNotOperational);
        failure.Message.Should().Contain("exit code 101").And.Contain("default toolchain").And.Contain("Repair").And.Contain("restart Visual Studio");
    }

    [Fact]
    public async Task ReturnsSuccessAndUsesConfiguredDefaultToolchainExplicitlyAsync()
    {
        var probe = new FakePrerequisiteProbe();

        var result = await new PrerequisiteEvaluator(probe).EvaluateAsync(default);

        result.Should().BeSameAs(PrerequisiteResult.Success);
        probe.SearchedFileNames.Should().Equal(Constants.RustUpExe, Constants.CargoExe);
        probe.Commands.Should().Equal(
            $"{probe.RustupPath}|--version",
            $"{probe.RustupPath}|default",
            $"{probe.CargoPath}|+stable-x86_64-pc-windows-msvc|--version");
        probe.Commands.Should().NotContain(command => command.Contains("nightly"));
    }

    [Fact]
    public async Task AggregatesIndependentFailuresAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            VisualStudioVersion = new Version(17, 11),
            RustupPath = null,
            CargoPath = null,
        };

        var result = await new PrerequisiteEvaluator(probe).EvaluateAsync(default);

        result.IsSuccess.Should().BeFalse();
        result.Failures.Select(failure => failure.Kind).Should().Equal(
            PrerequisiteFailureKind.UnsupportedVisualStudioHost,
            PrerequisiteFailureKind.RustupNotFound,
            PrerequisiteFailureKind.CargoNotFound);
        probe.SearchedFileNames.Should().Equal(Constants.RustUpExe, Constants.CargoExe);
        probe.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task RustupFailureSuppressesDependentProbesAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            CargoPath = null,
            RustupVersionResult = PrerequisiteCommandResult.Completed(1, string.Empty, "rustup failed"),
        };

        var result = await new PrerequisiteEvaluator(probe).EvaluateAsync(default);

        result.Failures.Select(failure => failure.Kind).Should().Equal(
            PrerequisiteFailureKind.RustupNotOperational,
            PrerequisiteFailureKind.CargoNotFound);
        probe.Commands.Should().Equal($"{probe.RustupPath}|--version");
    }

    [Fact]
    public async Task DefaultToolchainFailureSuppressesCargoOperationAsync()
    {
        var probe = new FakePrerequisiteProbe
        {
            RustupDefaultResult = PrerequisiteCommandResult.Completed(1, string.Empty, "no default toolchain configured"),
        };

        var result = await new PrerequisiteEvaluator(probe).EvaluateAsync(default);

        result.Failures.Select(failure => failure.Kind).Should().Equal(PrerequisiteFailureKind.DefaultToolchainNotConfigured);
        probe.Commands.Should().Equal(
            $"{probe.RustupPath}|--version",
            $"{probe.RustupPath}|default");
    }

    [Fact]
    public async Task PropagatesCancellationBeforeProbingAsync()
    {
        var probe = new FakePrerequisiteProbe();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> evaluate = () => new PrerequisiteEvaluator(probe).EvaluateAsync(cancellation.Token);

        await evaluate.Should().ThrowAsync<OperationCanceledException>();
        probe.SearchedFileNames.Should().BeEmpty();
        probe.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PropagatesCancellationFromProbeAsync()
    {
        var probe = new FakePrerequisiteProbe();
        using var cancellation = new CancellationTokenSource();
        probe.CancelOnRun = cancellation;

        Func<Task> evaluate = () => new PrerequisiteEvaluator(probe).EvaluateAsync(cancellation.Token);

        await evaluate.Should().ThrowAsync<OperationCanceledException>();
        probe.Commands.Should().ContainSingle().Which.Should().Be($"{probe.RustupPath}|--version");
    }

    [Fact]
    public async Task PropagatesEvaluatorInfrastructureFaultAsync()
    {
        var expected = new InvalidOperationException("Probe infrastructure failed.");
        var probe = new FakePrerequisiteProbe
        {
            RunException = expected,
        };

        Func<Task> evaluate = () => new PrerequisiteEvaluator(probe).EvaluateAsync(default);

        var exception = await evaluate.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public void FailureAndCommandResultsExposeNoMutableProperties()
    {
        typeof(PrerequisiteFailure)
            .GetProperties()
            .Should()
            .OnlyContain(property => property.SetMethod == null);
        typeof(PrerequisiteCommandResult)
            .GetProperties()
            .Should()
            .OnlyContain(property => property.SetMethod == null);

        var failure = new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found.");
        failure.Check.Should().Be(nameof(PrerequisiteFailureKind.CargoNotFound));
        failure.Kind.Should().Be(PrerequisiteFailureKind.CargoNotFound);
    }

    private static async Task<PrerequisiteFailure> EvaluateSingleFailureAsync(FakePrerequisiteProbe probe)
    {
        var result = await new PrerequisiteEvaluator(probe).EvaluateAsync(default);
        result.IsSuccess.Should().BeFalse();
        result.Failures.Should().ContainSingle();
        return result.Failures[0];
    }

    private sealed class FakePrerequisiteProbe : IPrerequisiteProbe
    {
        private const string DefaultRustupPath = @"C:\tools\rustup.exe";
        private const string DefaultCargoPath = @"C:\tools\cargo.exe";

        public Version VisualStudioVersion { get; set; } = new Version(17, 12);

        public string RustupPath { get; set; } = DefaultRustupPath;

        public string CargoPath { get; set; } = DefaultCargoPath;

        public PrerequisiteCommandResult RustupVersionResult { get; set; } =
            PrerequisiteCommandResult.Completed(0, "rustup 1.28.0", string.Empty);

        public PrerequisiteCommandResult RustupDefaultResult { get; set; } =
            PrerequisiteCommandResult.Completed(0, "stable-x86_64-pc-windows-msvc (default)", string.Empty);

        public PrerequisiteCommandResult CargoVersionResult { get; set; } =
            PrerequisiteCommandResult.Completed(0, "cargo 1.89.0", string.Empty);

        public CancellationTokenSource CancelOnRun { get; set; }

        public Exception RunException { get; set; }

        public List<string> SearchedFileNames { get; } = new List<string>();

        public List<string> Commands { get; } = new List<string>();

        Task<Version> IPrerequisiteProbe.GetVisualStudioVersionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(VisualStudioVersion);
        }

        string IPrerequisiteProbe.FindExecutable(string fileName)
        {
            SearchedFileNames.Add(fileName);
            if (fileName == Constants.RustUpExe)
            {
                return RustupPath;
            }

            if (fileName == Constants.CargoExe)
            {
                return CargoPath;
            }

            throw new InvalidOperationException($"Unexpected executable: {fileName}");
        }

        Task<PrerequisiteCommandResult> IPrerequisiteProbe.RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Commands.Add($"{executablePath}|{string.Join("|", arguments)}");
            if (CancelOnRun != null)
            {
                CancelOnRun.Cancel();
                return Task.FromCanceled<PrerequisiteCommandResult>(CancelOnRun.Token);
            }

            if (RunException != null)
            {
                return Task.FromException<PrerequisiteCommandResult>(RunException);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (executablePath == RustupPath && arguments.SequenceEqual(new[] { "--version" }))
            {
                return Task.FromResult(RustupVersionResult);
            }

            if (executablePath == RustupPath && arguments.SequenceEqual(new[] { "default" }))
            {
                return Task.FromResult(RustupDefaultResult);
            }

            if (executablePath == CargoPath &&
                arguments.SequenceEqual(new[] { "+stable-x86_64-pc-windows-msvc", "--version" }))
            {
                return Task.FromResult(CargoVersionResult);
            }

            throw new InvalidOperationException($"Unexpected command: {Commands.Last()}");
        }
    }
}
