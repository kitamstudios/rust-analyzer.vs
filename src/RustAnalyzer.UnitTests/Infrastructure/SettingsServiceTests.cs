using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;
using Moq;
using Xunit;
using MelEventId = Microsoft.Extensions.Logging.EventId;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class SettingsServiceTests
{
    [Fact]
    public async Task PersistenceFailureUsesStructuredOwnerEventAsync()
    {
        var expected =
            new InvalidOperationException("Settings persistence failed.");
        var settingsManager = new Mock<IWorkspaceSettingsManager>();
        settingsManager
            .Setup(manager => manager.GetPersistanceAsync(true))
            .ThrowsAsync(expected);
        using var provider = new RecordingLoggerProvider();
        using var factory = new Microsoft.Extensions.Logging.LoggerFactory(
            new[] { provider, });
        var legacyLogger = new RecordingLegacyLogger();
        var service = (ISettingsService)new SettingsServiceFactory
        {
            L = legacyLogger,
            LoggerFactory = factory,
        }.CreateService(CreateWorkspace(settingsManager.Object).Object);

        await service.SetAsync(
            SettingsInfo.TypeCommandLineArguments,
            (PathEx)@"C:\workspace\Cargo.toml",
            "--release");

        var entry = provider.Entries.Should().ContainSingle().Which;
        entry.Category.Should().Be(typeof(SettingsService).FullName);
        entry.EventId.Should().Be(
            new MelEventId(1, "SettingsPersistenceFailed"));
        entry.Level.Should().Be(MelLogLevel.Error);
        entry.Template.Should().Be("Exception.");
        entry.Exception.Should().BeSameAs(expected);
        legacyLogger.Errors.Should().BeEmpty();
        legacyLogger.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyFactoryDeliversFailureOnceAsync()
    {
        var expected =
            new InvalidOperationException("Settings persistence failed.");
        var settingsManager = new Mock<IWorkspaceSettingsManager>();
        settingsManager
            .Setup(manager => manager.GetPersistanceAsync(true))
            .ThrowsAsync(expected);
        var logger = new RecordingLegacyLogger();
        var service = (ISettingsService)new SettingsServiceFactory
        {
            L = logger,
        }.CreateService(CreateWorkspace(settingsManager.Object).Object);

        await service.SetAsync(
            SettingsInfo.TypeCommandLineArguments,
            (PathEx)@"C:\workspace\Cargo.toml",
            "--release");

        logger.Errors.Should().ContainSingle();
        logger.Errors[0].Format.Should().Be("{0} {1}");
        logger.Errors[0].Arguments.Should().HaveCount(2);
        logger.Errors[0].Arguments[0].Should().Be("Exception.");
        logger.Errors[0].Arguments[1].Should().BeSameAs(expected);
        logger.Lines.Should().BeEmpty();
    }

    private static Mock<IWorkspace> CreateWorkspace(
        IWorkspaceSettingsManager settingsManager)
    {
        var workspace = new Mock<IWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(value => value.Location)
            .Returns(@"C:\workspace");
        workspace.Setup(value => value.GetService(
                typeof(IWorkspaceSettingsManager)))
            .Returns(settingsManager);
        return workspace;
    }

    private sealed class RecordingLegacyLogger : ILogger
    {
        public List<(string Format, object[] Arguments)> Errors { get; } =
            new();

        public List<(string Format, object[] Arguments)> Lines { get; } =
            new();

        public void WriteLine(string format, params object[] args)
        {
            Lines.Add((format, args));
        }

        public void WriteError(string format, params object[] args)
        {
            Errors.Add((format, args));
        }
    }
}
