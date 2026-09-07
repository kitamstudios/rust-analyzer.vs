using System;
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
        var service = (ISettingsService)new SettingsServiceFactory
        {
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
}
