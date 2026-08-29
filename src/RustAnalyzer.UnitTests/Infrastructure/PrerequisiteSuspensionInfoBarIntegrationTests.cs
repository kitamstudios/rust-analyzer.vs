using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "IntegrationTests")]
public sealed class PrerequisiteSuspensionInfoBarIntegrationTests
{
    [Fact]
    public void ModelComposesTheExactWarningInfoBar()
    {
        var model = new PrerequisiteSuspensionInfoBarModel();

        var infoBarModel = model.CreateInfoBarModel();

        infoBarModel.TextSpans.Count.Should().Be(1);
        infoBarModel.TextSpans.GetSpan(0).Text.Should().Be(
            "rust-analyzer.vs is disabled for this Visual Studio session. Restart Visual Studio to recheck prerequisites.");
        infoBarModel.Image.Should().Be(KnownMonikers.StatusWarning);
        infoBarModel.IsCloseButtonVisible.Should().BeTrue();
        infoBarModel.ActionItems.Count.Should().Be(1);
        var action = infoBarModel.ActionItems.GetItem(0);
        action.Should().BeOfType<InfoBarHyperlink>();
        action.Text.Should().Be("View prerequisites");
        action.IsButton.Should().BeFalse();
        model.IsViewPrerequisitesAction(action.ActionContext).Should().BeTrue();
    }

    [Fact]
    public async Task VisualStudioInfoBarShowsRoutesItsActionAndReleasesEventsOnCloseAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        var result = PrerequisiteResult.Failed(
            new[]
            {
                new PrerequisiteFailure(
                    PrerequisiteFailureKind.CargoNotFound,
                    "Cargo was not found."),
            });
        await state.GetOrEvaluateAsync(_ => Task.FromResult(result), default);
        state.Suspend();
        var host = new TestInfoBarHost();
        var uiFactory = new TestInfoBarUiFactory();
        var openedUrls = new List<string>();
        InfoBarModel capturedModel = null;
        var creations = 0;
        var notification = new PrerequisiteSuspensionNotification(
            model =>
            {
                creations++;
                capturedModel = model;
                return Task.FromResult<IPrerequisiteSuspensionInfoBar>(
                    new VisualStudioPrerequisiteSuspensionInfoBar(
                        context.Factory,
                        host,
                        uiFactory,
                        model));
            },
            openedUrls.Add);

        (await notification.ShowIfSuspendedAsync(state)).Should().BeTrue();

        creations.Should().Be(1);
        uiFactory.Model.Should().BeSameAs(capturedModel);
        uiFactory.Element.AdviseCount.Should().Be(1);
        host.AddCount.Should().Be(1);
        host.AddedElement.Should().BeSameAs(uiFactory.Element);
        openedUrls.Should().BeEmpty();

        uiFactory.Element.RaiseAction(capturedModel.ActionItems.GetItem(0));

        openedUrls.Should().Equal(Constants.PrerequisitesUrl);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().BeSameAs(result);

        uiFactory.Element.Close();
        uiFactory.Element.RaiseAction(capturedModel.ActionItems.GetItem(0));

        uiFactory.Element.UnadviseCount.Should().Be(1);
        openedUrls.Should().Equal(Constants.PrerequisitesUrl);
        (await notification.ShowIfSuspendedAsync(state)).Should().BeTrue();
        creations.Should().Be(1);
        host.AddCount.Should().Be(1);
    }

    [Fact]
    public void VisualStudioInfoBarRejectsEventsOffTheOwningUiThread()
    {
        var ownerThread = Thread.CurrentThread;
        var ownerContext = new SingleThreadedSynchronizationContext();
        using var context = new JoinableTaskContext(ownerThread, ownerContext);
        using var infoBar = new VisualStudioPrerequisiteSuspensionInfoBar(
            context.Factory,
            new TestInfoBarHost(),
            new TestInfoBarUiFactory(),
            new PrerequisiteSuspensionInfoBarModel().CreateInfoBarModel());
        var eventSink = (IVsInfoBarUIEvents)infoBar;
        Thread callbackThread = null;
        Exception callbackException = null;
        var thread = new Thread(
            () =>
            {
                callbackThread = Thread.CurrentThread;
                callbackException = Record.Exception(
                    () => eventSink.OnActionItemClicked(
                        new TestInfoBarUiElement(),
                        new InfoBarHyperlink("Unexpected", new object())));
            });

        thread.Start();
        thread.Join();

        callbackThread.Should().NotBeSameAs(ownerThread);
        callbackException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task VisualStudioInfoBarUnadvisesWhenTheHostRejectsShowAsync()
    {
        using var context = new JoinableTaskContext();
        var expected = new InvalidOperationException("The host rejected the InfoBar.");
        var host = new TestInfoBarHost
        {
            AddException = expected,
        };
        var uiFactory = new TestInfoBarUiFactory();
        using var infoBar = new VisualStudioPrerequisiteSuspensionInfoBar(
            context.Factory,
            host,
            uiFactory,
            new PrerequisiteSuspensionInfoBarModel().CreateInfoBarModel());
        Func<Task> show = async () => await infoBar.TryShowAsync();

        var exception = await show.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Should().BeSameAs(expected);
        host.AddCount.Should().Be(1);
        uiFactory.Element.AdviseCount.Should().Be(1);
        uiFactory.Element.UnadviseCount.Should().Be(1);
    }

    private sealed class TestInfoBarHost : IVsInfoBarHost
    {
        public int AddCount { get; private set; }

        public IVsUIElement AddedElement { get; private set; }

        public Exception AddException { get; set; }

        public void AddInfoBar(IVsUIElement infoBar)
        {
            AddCount++;
            AddedElement = infoBar;
            if (AddException != null)
            {
                throw AddException;
            }
        }

        public void RemoveInfoBar(IVsUIElement infoBar)
        {
        }
    }

    private sealed class TestInfoBarUiFactory : IVsInfoBarUIFactory
    {
        public TestInfoBarUiElement Element { get; } = new();

        public InfoBarModel Model { get; private set; }

        public IVsInfoBarUIElement CreateInfoBar(IVsInfoBar infoBar)
        {
            Model = (InfoBarModel)infoBar;
            return Element;
        }
    }

    private sealed class TestInfoBarUiElement : IVsInfoBarUIElement
    {
        private IVsInfoBarUIEvents _events;
        private uint _cookie;

        public int AdviseCount { get; private set; }

        public int UnadviseCount { get; private set; }

        public int Advise(IVsInfoBarUIEvents eventSink, out uint cookie)
        {
            AdviseCount++;
            _events = eventSink;
            _cookie = 1;
            cookie = _cookie;
            return VSConstants.S_OK;
        }

        public int Unadvise(uint cookie)
        {
            cookie.Should().Be(_cookie);
            UnadviseCount++;
            _events = null;
            return VSConstants.S_OK;
        }

        public int Close()
        {
            _events?.OnClosed(this);
            return VSConstants.S_OK;
        }

        public int GetUIObject(out object uiObject)
        {
            uiObject = null;
            return VSConstants.E_NOTIMPL;
        }

        public int TranslateAccelerator(IVsUIAccelerator accelerator)
        {
            return VSConstants.S_FALSE;
        }

        public int get_DataSource(out IVsUISimpleDataSource dataSource)
        {
            dataSource = null;
            return VSConstants.E_NOTIMPL;
        }

        public int put_DataSource(IVsUISimpleDataSource dataSource)
        {
            return VSConstants.E_NOTIMPL;
        }

        public void RaiseAction(IVsInfoBarActionItem actionItem)
        {
            _events?.OnActionItemClicked(this, actionItem);
        }
    }
}
