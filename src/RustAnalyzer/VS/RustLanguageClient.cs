using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace KS.RustAnalyzer.VS;

[ContentType(RustConstants.RustLanguageContentType)]
[Export(typeof(ILanguageClient))]
[RunOnContext(RunningContext.RunOnHost)]
public class RustLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
{
    private readonly IVsFolderWorkspaceService _workspaceService;
    private readonly ILogger _logger;
    private readonly ITelemetryService _telemetryService;

    [ImportingConstructor]
    public RustLanguageClient([Import] IVsFolderWorkspaceService workspaceService, ILogger logger, ITelemetryService telemetryService)
    {
        _workspaceService = workspaceService;
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public event AsyncEventHandler<EventArgs> StartAsync;

    public event AsyncEventHandler<EventArgs> StopAsync;

    public JsonRpc Rpc
    {
        get;
        set;
    }

    public string Name => "Rust Language Extension";

    public IEnumerable<string> ConfigurationSections
    {
        get
        {
            yield return RustConstants.ConfigurationSectionName;
        }
    }

    public object InitializationOptions => null;

    public IEnumerable<string> FilesToWatch => null;

    public object MiddleLayer => new RustLanguageExtensionMiddleLayer(_logger);

    public object CustomMessageTarget => null;

    public bool ShowNotificationOnInitializeFailed => true;

    public async Task<Connection> ActivateAsync(CancellationToken token)
    {
        var programPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rust-analyzer.exe");
        _logger.WriteLine("Starting rust-analyzer from path: {0}.", programPath);
        ProcessStartInfo info = new ()
        {
            FileName = programPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Minimized,
            WorkingDirectory = _workspaceService.CurrentWorkspace.Location,
        };

        Process process = new ()
        {
            StartInfo = info
        };

        if (process.Start())
        {
            _logger.WriteLine("Done starting rust-analyzer from path.");
            _telemetryService.TrackEvent("rust-analyzer-start", ("Path", programPath));

            return await Task.FromResult(new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
        }

        _logger.WriteLine("Error starting rust-analyzer from path.");
        _telemetryService.TrackEvent("rust-analyzer-start-failure", ("Path", programPath));
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

        _logger.WriteLine(message);
        _telemetryService.TrackEvent("rust-analyzer-start-failure", ("exception", exception.ToString()));

        var failureContext = new InitializationFailureContext()
        {
            FailureMessage = message,
        };

        return Task.FromResult(failureContext);
    }

    public class RustLanguageExtensionMiddleLayer : ILanguageClientMiddleLayer
    {
        private readonly ILogger _logger;

        public RustLanguageExtensionMiddleLayer(ILogger logger)
        {
            _logger = logger;
        }

        public bool CanHandle(string methodName) => true;

        public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification)
        {
            _logger.WriteLine("HandleNotificationAsync: {0}", methodName);
            return sendNotification(methodParam);
        }

        public Task<JToken> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken>> sendRequest)
        {
            _logger.WriteLine("HandleRequestAsync: {0}", methodName);
            return sendRequest(methodParam);
        }
    }
}
