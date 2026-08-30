using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.Infrastructure;

public sealed class PrerequisiteSuspensionInfoBarModel
{
    private readonly ViewPrerequisitesActionContext _viewPrerequisitesActionContext = new();

    public string Text { get; } =
        "rust-analyzer.vs is disabled for this Visual Studio session. Restart Visual Studio to recheck prerequisites.";

    public string ViewPrerequisitesLabel { get; } = "View prerequisites";

    public InfoBarModel CreateInfoBarModel()
    {
        return new InfoBarModel(
            textSpans: new[] { new InfoBarTextSpan(Text), },
            actionItems: new[] { new InfoBarHyperlink(ViewPrerequisitesLabel, _viewPrerequisitesActionContext), },
            image: KnownMonikers.StatusWarning,
            isCloseButtonVisible: true);
    }

    public bool IsViewPrerequisitesAction(object actionContext)
    {
        return ReferenceEquals(_viewPrerequisitesActionContext, actionContext);
    }

    private sealed class ViewPrerequisitesActionContext
    {
    }
}

public sealed class PrerequisiteSuspensionInfoBarActionEventArgs : EventArgs
{
    public PrerequisiteSuspensionInfoBarActionEventArgs(object actionContext)
    {
        ActionContext = actionContext;
    }

    public object ActionContext { get; }
}

public interface IPrerequisiteSuspensionInfoBar : IDisposable
{
    event EventHandler<PrerequisiteSuspensionInfoBarActionEventArgs> ActionItemClicked;

    event EventHandler Closed;

    Task<bool> TryShowAsync();
}

public sealed class PrerequisiteSuspensionNotification
{
    private readonly object _sync = new();
    private readonly Func<InfoBarModel, Task<IPrerequisiteSuspensionInfoBar>> _createInfoBarAsync;
    private readonly Action<string> _openSystemBrowser;
    private readonly PrerequisiteSuspensionInfoBarModel _model = new();
    private Task<bool> _showTask;

    public PrerequisiteSuspensionNotification(
        Func<InfoBarModel, Task<IPrerequisiteSuspensionInfoBar>> createInfoBarAsync,
        Action<string> openSystemBrowser)
    {
        _createInfoBarAsync = EnsureArg.IsNotNull(
            createInfoBarAsync,
            nameof(createInfoBarAsync),
            options => options.WithException(new ArgumentNullException(nameof(createInfoBarAsync))));
        _openSystemBrowser = EnsureArg.IsNotNull(
            openSystemBrowser,
            nameof(openSystemBrowser),
            options => options.WithException(new ArgumentNullException(nameof(openSystemBrowser))));
    }

    public static PrerequisiteSuspensionNotification Current => ProcessNotificationHolder.Instance;

    public static bool IsEligible(PrerequisiteStatus status)
    {
        return status == PrerequisiteStatus.Suspended;
    }

    public Task<bool> ShowIfSuspendedAsync(PrerequisiteProcessState state)
    {
        EnsureArg.IsNotNull(
            state,
            nameof(state),
            options => options.WithException(new ArgumentNullException(nameof(state))));

        lock (_sync)
        {
            if (!IsEligible(state.Status))
            {
                return Task.FromResult(false);
            }

            _showTask ??= ShowAsync();
            return _showTask;
        }
    }

    private async Task<bool> ShowAsync()
    {
        var infoBar = await _createInfoBarAsync(_model.CreateInfoBarModel());
        if (infoBar == null)
        {
            throw new InvalidOperationException("The prerequisite suspension InfoBar could not be created.");
        }

        EventHandler<PrerequisiteSuspensionInfoBarActionEventArgs> actionHandler = null;
        EventHandler closedHandler = null;
        actionHandler = (sender, e) =>
        {
            if (ReferenceEquals(sender, infoBar) &&
                e != null &&
                _model.IsViewPrerequisitesAction(e.ActionContext))
            {
                _openSystemBrowser(Constants.PrerequisitesUrl);
            }
        };
        closedHandler = (sender, _) =>
        {
            if (ReferenceEquals(sender, infoBar))
            {
                DetachAndDispose(infoBar, actionHandler, closedHandler);
            }
        };

        infoBar.ActionItemClicked += actionHandler;
        infoBar.Closed += closedHandler;
        try
        {
            if (!await infoBar.TryShowAsync())
            {
                throw new InvalidOperationException("The prerequisite suspension InfoBar could not be shown.");
            }

            return true;
        }
        catch
        {
            DetachAndDispose(infoBar, actionHandler, closedHandler);
            throw;
        }
    }

    private static void DetachAndDispose(
        IPrerequisiteSuspensionInfoBar infoBar,
        EventHandler<PrerequisiteSuspensionInfoBarActionEventArgs> actionHandler,
        EventHandler closedHandler)
    {
        infoBar.ActionItemClicked -= actionHandler;
        infoBar.Closed -= closedHandler;
        infoBar.Dispose();
    }

    private static class ProcessNotificationHolder
    {
        public static PrerequisiteSuspensionNotification Instance { get; } = new(
            VisualStudioPrerequisiteSuspensionInfoBar.CreateForMainWindowAsync,
            url => VsShellUtilities.OpenSystemBrowser(url));
    }
}

public sealed class VisualStudioPrerequisiteSuspensionInfoBar :
    IPrerequisiteSuspensionInfoBar,
    IVsInfoBarUIEvents
{
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly IVsInfoBarHost _host;
    private readonly IVsInfoBarUIFactory _uiFactory;
    private readonly InfoBarModel _model;
    private IVsInfoBarUIElement _uiElement;
    private uint _listenerCookie;
    private bool _advised;
    private bool _disposed;
    private bool _showAttempted;

    public VisualStudioPrerequisiteSuspensionInfoBar(
        JoinableTaskFactory joinableTaskFactory,
        IVsInfoBarHost host,
        IVsInfoBarUIFactory uiFactory,
        InfoBarModel model)
    {
        _joinableTaskFactory = EnsureArg.IsNotNull(
            joinableTaskFactory,
            nameof(joinableTaskFactory),
            options => options.WithException(new ArgumentNullException(nameof(joinableTaskFactory))));
        _host = EnsureArg.IsNotNull(
            host,
            nameof(host),
            options => options.WithException(new ArgumentNullException(nameof(host))));
        _uiFactory = EnsureArg.IsNotNull(
            uiFactory,
            nameof(uiFactory),
            options => options.WithException(new ArgumentNullException(nameof(uiFactory))));
        _model = EnsureArg.IsNotNull(
            model,
            nameof(model),
            options => options.WithException(new ArgumentNullException(nameof(model))));
    }

    public event EventHandler<PrerequisiteSuspensionInfoBarActionEventArgs> ActionItemClicked;

    public event EventHandler Closed;

    public static async Task<IPrerequisiteSuspensionInfoBar> CreateForMainWindowAsync(InfoBarModel model)
    {
        EnsureArg.IsNotNull(
            model,
            nameof(model),
            options => options.WithException(new ArgumentNullException(nameof(model))));

        await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();

        var shell = await CommunityVS.Services.GetShellAsync();
        ErrorHandler.ThrowOnFailure(
            shell.GetProperty(
                (int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost,
                out var hostValue));
        if (hostValue is not IVsInfoBarHost host)
        {
            return null;
        }

        var uiFactory = await CommunityVS.Services.GetInfoBarUIFactoryAsync() as IVsInfoBarUIFactory;
        if (uiFactory == null)
        {
            return null;
        }

        return new VisualStudioPrerequisiteSuspensionInfoBar(
            RustAnalyzerPackage.JTF,
            host,
            uiFactory,
            model);
    }

    public async Task<bool> TryShowAsync()
    {
        await _joinableTaskFactory.SwitchToMainThreadAsync();

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VisualStudioPrerequisiteSuspensionInfoBar));
        }

        if (_showAttempted)
        {
            throw new InvalidOperationException("This InfoBar has already been shown.");
        }

        _showAttempted = true;
        _uiElement = _uiFactory.CreateInfoBar(_model);
        if (_uiElement == null)
        {
            return false;
        }

        var adviseResult = _uiElement.Advise(this, out _listenerCookie);
        ErrorHandler.ThrowOnFailure(adviseResult);
        _advised = true;

        try
        {
            _host.AddInfoBar(_uiElement);
            return true;
        }
        catch
        {
            Unadvise();
            _uiElement = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_advised)
        {
            throw new InvalidOperationException("A visible InfoBar cannot be disposed.");
        }

        _uiElement = null;
        ActionItemClicked = null;
        Closed = null;
        _disposed = true;
    }

    void IVsInfoBarUIEvents.OnActionItemClicked(
        IVsInfoBarUIElement infoBarUIElement,
        IVsInfoBarActionItem actionItem)
    {
        ThrowIfNotOnUIThread();
        if (_disposed || _uiElement == null)
        {
            return;
        }

        ActionItemClicked?.Invoke(
            this,
            new PrerequisiteSuspensionInfoBarActionEventArgs(actionItem?.ActionContext));
    }

    void IVsInfoBarUIEvents.OnClosed(IVsInfoBarUIElement infoBarUIElement)
    {
        ThrowIfNotOnUIThread();
        if (_disposed || _uiElement == null)
        {
            return;
        }

        Unadvise();
        _uiElement = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Unadvise()
    {
        ThrowIfNotOnUIThread();

        if (!_advised)
        {
            return;
        }

        var result = _uiElement.Unadvise(_listenerCookie);
        _advised = false;
        ErrorHandler.ThrowOnFailure(result);
    }

    private void ThrowIfNotOnUIThread()
    {
        if (!_joinableTaskFactory.Context.IsOnMainThread)
        {
            throw new InvalidOperationException("The InfoBar event must be handled on the UI thread.");
        }
    }
}
