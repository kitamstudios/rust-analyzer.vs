using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using StreamJsonRpc;

namespace KS.RustAnalyzer.LanguageService;

[ContentType(Constants.RustLanguageContentType)]
[Export(typeof(ILanguageClient))]
[RunOnContext(RunningContext.RunOnHost)]
public class LanguageClient : ILanguageClient, ILanguageClientCustomMessage2, IDisposable
{
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly AsyncLazy<object> _loading;
    private readonly object _sync = new();
    private Stopwatch _activationDuration;
    private bool _activationReported;
    private bool _disposed;
    private bool _stopped;
    private Task _stopping;

    public LanguageClient()
        : this(RustAnalyzerPackage.JTF)
    {
    }

    public LanguageClient(JoinableTaskFactory joinableTaskFactory)
    {
        EnsureArg.IsNotNull(joinableTaskFactory);
        _lifetimeToken = _lifetimeCancellation.Token;
        _loading = new AsyncLazy<object>(
            async () =>
            {
                await OnLoadedCoreAsync();
                return null;
            },
            joinableTaskFactory);
    }

    public event AsyncEventHandler<EventArgs> StartAsync;

    public event AsyncEventHandler<EventArgs> StopAsync;

    [Import]
    public IVsFolderWorkspaceService WorkspaceService { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public IFeatureUsageTelemetry UsageTelemetry { get; set; }

    [Import]
    public IRlsInstallerService RADownloader { get; set; }

    [Import]
    public PrerequisiteAvailabilityPolicy AvailabilityPolicy { get; set; }

    public JsonRpc Rpc { get; set; }

    public string Name => "Rust Language Extension";

    public IEnumerable<string> ConfigurationSections
    {
        get
        {
            yield return Constants.ConfigurationSectionName;
        }
    }

    public object InitializationOptions => null;

    public IEnumerable<string> FilesToWatch => null;

    public object MiddleLayer => null;

    public object CustomMessageTarget => null;

    public bool ShowNotificationOnInitializeFailed =>
        !IsStopped &&
        AvailabilityPolicy.IsReady(AutomaticRustPath.LanguageClientActivation);

    public async Task<Connection> ActivateAsync(CancellationToken token)
    {
        if (IsStopped)
        {
            return null;
        }

        using var activationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
        try
        {
            if (!await AvailabilityPolicy.IsReadyAsync(
                    AutomaticRustPath.LanguageClientActivation,
                    activationCancellation.Token))
            {
                return null;
            }

            BeginActivation();
            var rlsPath = await RADownloader.GetExePathAsync();
            activationCancellation.Token.ThrowIfCancellationRequested();
            L.WriteLine("Starting rust-analyzer from path: {0}.", rlsPath);
            ProcessStartInfo info = new()
            {
                FileName = rlsPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = WorkspaceService.CurrentWorkspace?.Location ?? Path.GetDirectoryName(rlsPath),
            };

            Process process = new()
            {
                StartInfo = info
            };
            bool started;
            lock (_sync)
            {
                if (_stopped || _disposed)
                {
                    process.Dispose();
                    ReportActivation(UsageOutcome.Cancelled);
                    return null;
                }

                started = process.Start();
            }

            if (started)
            {
                L.WriteLine("Done starting rust-analyzer from path. PID: {0}", process.Id);
                return await Task.FromResult(new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
            }

            L.WriteLine("Error starting rust-analyzer from path.");
            ReportActivation(UsageOutcome.Failed);
            return null;
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested && !token.IsCancellationRequested)
        {
            ReportActivation(UsageOutcome.Cancelled);
            return null;
        }
        catch (OperationCanceledException)
        {
            ReportActivation(UsageOutcome.Cancelled);
            throw;
        }
        catch (Exception)
        {
            ReportActivation(UsageOutcome.Failed);
            throw;
        }
    }

    public Task OnLoadedAsync()
    {
        lock (_sync)
        {
            return _stopped || _disposed
                ? Task.CompletedTask
                : _loading.GetValueAsync();
        }
    }

    public Task StopServerAsync()
    {
        ReportActivation(UsageOutcome.Cancelled);
        lock (_sync)
        {
            if (_stopping != null)
            {
                return _stopping;
            }

            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _stopped = true;
            _lifetimeCancellation.Cancel();
            _stopping = StopAsync == null
                ? Task.CompletedTask
                : StopAsync.InvokeAsync(this, EventArgs.Empty);
            return _stopping;
        }
    }

    public void Dispose()
    {
        ReportActivation(UsageOutcome.Cancelled);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopped = true;
            _lifetimeCancellation.Cancel();
        }

        _lifetimeCancellation.Dispose();
    }

    public Task OnServerInitializedAsync()
    {
        ReportActivation(UsageOutcome.Succeeded);
        return Task.CompletedTask;
    }

    public Task AttachForCustomMessageAsync(JsonRpc rpc)
    {
        Rpc = rpc;

        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        ReportActivation(UsageOutcome.Failed);
        if (IsStopped ||
            !AvailabilityPolicy.IsReady(AutomaticRustPath.LanguageClientActivation))
        {
            return Task.FromResult<InitializationFailureContext>(null);
        }

        string message = "Oh no! rust-analyzer failed to activate, now we can't test LSP! :(";
        string exception = initializationState.InitializationException?.ToString() ?? string.Empty;
        message = $"{message}\n {exception}";

        L.WriteLine(message);

        var failureContext = new InitializationFailureContext()
        {
            FailureMessage = message,
        };

        return Task.FromResult(failureContext);
    }

    private bool IsStopped
    {
        get
        {
            lock (_sync)
            {
                return _stopped || _disposed;
            }
        }
    }

    private void BeginActivation()
    {
        lock (_sync)
        {
            _activationDuration = Stopwatch.StartNew();
            _activationReported = false;
        }
    }

    private void ReportActivation(UsageOutcome outcome)
    {
        Stopwatch duration;
        lock (_sync)
        {
            if (_activationDuration == null || _activationReported)
            {
                return;
            }

            _activationReported = true;
            duration = _activationDuration;
        }

        UsageTelemetry.Track(
            UsageOperation.LanguageServerActivate,
            outcome,
            duration.Elapsed);
    }

    private async Task OnLoadedCoreAsync()
    {
        bool isReady;
        try
        {
            isReady = await AvailabilityPolicy.WaitForReadyAsync(
                AutomaticRustPath.LanguageClientActivation,
                _lifetimeToken);
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        if (!isReady)
        {
            return;
        }

        Task start;
        lock (_sync)
        {
            if (_stopped || _disposed || StartAsync == null)
            {
                return;
            }

            start = StartAsync.InvokeAsync(this, EventArgs.Empty);
        }

        await start;
    }
}
