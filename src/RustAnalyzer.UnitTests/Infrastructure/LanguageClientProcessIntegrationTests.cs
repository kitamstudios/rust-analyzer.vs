using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.LanguageService;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using Moq;
using Xunit;
using MelEventId = Microsoft.Extensions.Logging.EventId;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "IntegrationTests")]
public sealed class LanguageClientProcessIntegrationTests
{
    [Fact]
    public async Task ProcessStartPreservesLspTransportAndStructuredEventsAsync()
    {
        using var context = new JoinableTaskContext();
        using var provider = new RecordingLoggerProvider();
        using var factory = new Microsoft.Extensions.Logging.LoggerFactory(
            new[] { provider, });
        using var client = new ProcessLanguageClient(context.Factory)
        {
            L = Mock.Of<ILogger>(),
            LoggerFactory = factory,
            WorkspaceService = Mock.Of<IVsFolderWorkspaceService>(),
        };
        var serverPath = (PathEx)Environment.GetEnvironmentVariable("ComSpec");

        using var connection = await client.StartBaseServerAsync(
            serverPath,
            default);
        using (var writer = new StreamWriter(
                   connection.Writer,
                   Encoding.ASCII,
                   1024,
                   leaveOpen: true)
               {
                   AutoFlush = true,
               })
        {
            writer.WriteLine("echo LSP_TRANSPORT");
            writer.WriteLine("exit");
        }

        string output;
        using (var reader = new StreamReader(
                   connection.Reader,
                   Encoding.ASCII,
                   false,
                   1024,
                   leaveOpen: true))
        {
            output = await reader.ReadToEndAsync();
        }

        output.Should().Contain("LSP_TRANSPORT");
        provider.Entries.Should().HaveCount(2);
        var starting = GetEntry(
            provider,
            new MelEventId(2, "ServerStarting"),
            MelLogLevel.Information,
            "Starting rust-analyzer from path: {ServerPath}.");
        starting.Properties["ServerPath"].Should().Be(serverPath);
        starting.Exception.Should().BeNull();
        var started = GetEntry(
            provider,
            new MelEventId(4, "ServerStarted"),
            MelLogLevel.Information,
            "Done starting rust-analyzer from path. PID: {ProcessId}");
        started.Properties["ProcessId"].Should().BeOfType<int>()
            .Which.Should().BePositive();
        started.Exception.Should().BeNull();
        provider.Entries.Should().NotContain(
            entry => entry.Message.Contains("LSP_TRANSPORT"));
    }

    private static RecordingLogEntry GetEntry(
        RecordingLoggerProvider provider,
        MelEventId eventId,
        MelLogLevel level,
        string template)
    {
        var entry = provider.Entries.Single(
            candidate => candidate.Category == typeof(LanguageClient).FullName
                && candidate.EventId == eventId);
        entry.Level.Should().Be(level);
        entry.Template.Should().Be(template);
        return entry;
    }

    private sealed class ProcessLanguageClient : LanguageClient
    {
        public ProcessLanguageClient(
            JoinableTaskFactory joinableTaskFactory)
            : base(joinableTaskFactory)
        {
        }

        public Task<Connection> StartBaseServerAsync(
            PathEx serverPath,
            CancellationToken cancellationToken)
        {
            return StartServerAsync(serverPath, cancellationToken);
        }
    }
}
