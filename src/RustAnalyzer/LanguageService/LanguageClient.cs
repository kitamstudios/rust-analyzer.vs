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
using Newtonsoft.Json.Linq;
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
    public IRAInstallerService RADownloader { get; set; }

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

    public bool ShowNotificationOnInitializeFailed => true;

    public async Task<Connection> ActivateAsync(CancellationToken token)
    {
        var rlsPath = await RADownloader.GetRustAnalyzerExePathAsync();
        L.WriteLine("Starting rust-analyzer from path: {0}.", rlsPath);
        ProcessStartInfo info = new ()
        {
            FileName = rlsPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Minimized,
            WorkingDirectory = WorkspaceService.CurrentWorkspace?.Location ?? Path.GetDirectoryName(rlsPath),
        };

        Process process = new ()
        {
            StartInfo = info
        };

        if (process.Start())
        {
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

    public class LanguageExtensionMiddleLayer : ILanguageClientMiddleLayer
    {
        private readonly ILogger _logger;

        public LanguageExtensionMiddleLayer(ILogger logger)
        {
            _logger = logger;
        }

        public bool CanHandle(string methodName) => true;

        public async Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification)
        {
            _logger.WriteLine("HandleNotificationAsync: {0}", methodName);
            await sendNotification(methodParam);
        }

        public async Task<JToken> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken>> sendRequest)
        {
            _logger.WriteLine("HandleRequestAsync: {0}", methodName);
            return await sendRequest(methodParam);
        }
    }
}
