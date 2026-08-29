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
    public ITelemetryService T { get; set; }

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
                    return null;
                }

                started = process.Start();
            }

            if (started)
            {
                L.WriteLine("Done starting rust-analyzer from path. PID: {0}", process.Id);
                T.TrackEvent("rust-analyzer-start", ("Path", rlsPath));

                return await Task.FromResult(new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
            }

            L.WriteLine("Error starting rust-analyzer from path.");
            T.TrackException(new InvalidOperationException(), new[] { ("Path", (string)rlsPath) });
            return null;
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested && !token.IsCancellationRequested)
        {
            return null;
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
        return Task.CompletedTask;
    }

    public Task AttachForCustomMessageAsync(JsonRpc rpc)
    {
        Rpc = rpc;

        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        if (IsStopped ||
            !AvailabilityPolicy.IsReady(AutomaticRustPath.LanguageClientActivation))
        {
            return Task.FromResult<InitializationFailureContext>(null);
        }

        string message = "Oh no! rust-analyzer failed to activate, now we can't test LSP! :(";
        string exception = initializationState.InitializationException?.ToString() ?? string.Empty;
        message = $"{message}\n {exception}";

        L.WriteLine(message);
        T.TrackException(initializationState.InitializationException);

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
