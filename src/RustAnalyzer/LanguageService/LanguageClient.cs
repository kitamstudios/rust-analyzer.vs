using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
public class LanguageClient : ILanguageClient, ILanguageClientCustomMessage2
{
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

    public JsonRpc Rpc { get; set; }

    public string Name => "Rust Language Extension";

    public IEnumerable<string> ConfigurationSections
    {
        get
        {
            yield return Constants.ConfigurationSectionName;
        }
    }

    public object InitializationOptions { get; set; } = null;

    public IEnumerable<string> FilesToWatch => null;

    public object MiddleLayer => null;

    public object CustomMessageTarget => null;

    public bool ShowNotificationOnInitializeFailed => true;

    public async Task<Connection> ActivateAsync(CancellationToken token)
    {
        var options = await Options.GetLiveInstanceAsync();
        var rlsPath = await RADownloader.GetExePathAsync();
        L.WriteLine("Starting rust-analyzer from path: {0}.", rlsPath);
        ProcessStartInfo info = new()
        {
            FileName = rlsPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Minimized,
            WorkingDirectory = WorkspaceService.CurrentWorkspace?.Location ?? Path.GetDirectoryName(rlsPath),
        };

        foreach (var prop in options.GetRustAnalyzerEnvArguments().Properties())
        {
            // Value will never be null, see Options.cs::GetRustAnalyzerEnvArguments implementation
            info.EnvironmentVariables[prop.Name] = (string)prop.Value!;
        }

        Process process = new()
        {
            StartInfo = info
        };

        string topDir = WorkspaceService.CurrentWorkspace?.Location;
        var mergedOptions = options.GetMergedLspInitializationOptions(topDir);

        InitializationOptions = mergedOptions;
        L.WriteLine("Parsing lsp initializationOptions: {0}", InitializationOptions.SerializeObject(Newtonsoft.Json.Formatting.Indented));

        if (process.Start())
        {
            if (options.EnableRustAnalyzerStderrLogging)
            {
                _ = Task.Run(async () =>
                {
                    string line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        L.WriteLine("[rust-analyzer stderr] {0}", line);
                    }
                });
            }

            L.WriteLine("Done starting rust-analyzer from path. PID: {0}", process.Id);
            T.TrackEvent("rust-analyzer-start", ("Path", rlsPath));

            return await Task.FromResult(new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
        }

        L.WriteLine("Error starting rust-analyzer from path.");
        T.TrackException(new InvalidOperationException(), new[] { ("Path", (string)rlsPath) });
        return null;
    }

    public async Task OnLoadedAsync()
    {
        if (StartAsync != null)
        {
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }
    }

    public async Task StopServerAsync()
    {
        if (StopAsync != null)
        {
            await StopAsync.InvokeAsync(this, EventArgs.Empty);
        }
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
}
