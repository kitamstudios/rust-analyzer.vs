using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "IntegrationTests")]
public sealed class PrerequisiteFailureDialogIntegrationTests
{
    [Fact]
    public async Task DialogComposesExactOrderedContentAndActionsAsync()
    {
        using var context = new JoinableTaskContext();
        var state = await CreateFailedStateAsync(
            context,
            new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found."),
            new PrerequisiteFailure(PrerequisiteFailureKind.UnsupportedVisualStudioHost, "Visual Studio is unsupported."),
            new PrerequisiteFailure(PrerequisiteFailureKind.RustupNotFound, "rustup was not found."));
        var openedUrls = new List<string>();

        await RunOnStaThreadAsync(
            () =>
            {
                var controller = new PrerequisiteFailureDialogController(state, openedUrls.Add);
                var dialog = new PrerequisiteFailureDialog(controller);
                var buttons = FindLogicalDescendants<Button>(dialog.DialogContent).ToArray();
                var text = FindLogicalDescendants<TextBlock>(dialog.DialogContent)
                    .Select(textBlock => textBlock.Text)
                    .ToArray();

                dialog.Title.Should().Be("rust-analyzer.vs prerequisites failed");
                dialog.IsCloseButtonEnabled.Should().BeFalse();
                buttons.Should().HaveCount(2);
                buttons.Should().OnlyContain(button => button.Visibility == Visibility.Visible);
                buttons.Select(button => button.Content).Should().Equal("Disable", "Help");
                text.Should().Equal(
                    "rust-analyzer.vs cannot start because these prerequisites failed:",
                    "\u2022 Visual Studio is unsupported.",
                    "\u2022 rustup was not found.",
                    "\u2022 Cargo was not found.",
                    "Disable turns off rust-analyzer.vs for this Visual Studio session, closes this dialog, and returns control to Visual Studio. Restart Visual Studio to recheck prerequisites.",
                    "Help opens the prerequisite instructions in your system browser. This dialog stays open and prerequisite state does not change.");
                openedUrls.Should().BeEmpty();

                dialog.CloseForShutdown();
            });

        state.Status.Should().Be(PrerequisiteStatus.Failed);
    }

    [Fact]
    public async Task HelpButtonCanBeRepeatedWithoutClosingOrMutatingStateAsync()
    {
        using var context = new JoinableTaskContext();
        var state = await CreateFailedStateAsync(
            context,
            new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found."));
        var result = state.CachedResult;
        var openedUrls = new List<string>();

        await RunOnStaThreadAsync(
            () =>
            {
                var controller = new PrerequisiteFailureDialogController(state, openedUrls.Add);
                var dialog = new PrerequisiteFailureDialog(controller);
                var closed = false;
                dialog.Closed += (_, _) => closed = true;
                var help = FindLogicalDescendants<Button>(dialog.DialogContent)
                    .Single(button => Equals(button.Content, "Help"));

                help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                openedUrls.Should().Equal(Constants.PrerequisitesUrl, Constants.PrerequisitesUrl);
                closed.Should().BeFalse();
                state.Status.Should().Be(PrerequisiteStatus.Failed);
                state.CachedResult.Should().BeSameAs(result);

                dialog.CloseForShutdown();
            });
    }

    [Fact]
    public async Task DisableButtonSuspendsAndClosesWithoutNavigatingAsync()
    {
        using var context = new JoinableTaskContext();
        var state = await CreateFailedStateAsync(
            context,
            new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found."));
        var openedUrls = new List<string>();

        await RunOnStaThreadAsync(
            () =>
            {
                var controller = new PrerequisiteFailureDialogController(state, openedUrls.Add);
                var dialog = new PrerequisiteFailureDialog(controller);
                var closed = false;
                dialog.Closed += (_, _) => closed = true;
                var disable = FindLogicalDescendants<Button>(dialog.DialogContent)
                    .Single(button => Equals(button.Content, "Disable"));

                disable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                closed.Should().BeTrue();
                state.Status.Should().Be(PrerequisiteStatus.Suspended);
                openedUrls.Should().BeEmpty();
            });
    }

    [Fact]
    public async Task UserCloseGesturesAreBlockedButShutdownCanCloseAsync()
    {
        using var context = new JoinableTaskContext();
        var state = await CreateFailedStateAsync(
            context,
            new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found."));
        var openedUrls = new List<string>();

        await RunOnStaThreadAsync(
            () =>
            {
                var controller = new PrerequisiteFailureDialogController(state, openedUrls.Add);
                var dialog = new PrerequisiteFailureDialog(controller);
                var closed = false;
                dialog.Closed += (_, _) => closed = true;
                var escape = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new TestPresentationSource(),
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };

                dialog.RaiseEvent(escape);
                dialog.Close();
                dialog.Close();

                escape.Handled.Should().BeTrue();
                dialog.IsCloseButtonEnabled.Should().BeFalse();
                closed.Should().BeFalse();
                state.Status.Should().Be(PrerequisiteStatus.Failed);
                openedUrls.Should().BeEmpty();

                dialog.CloseForShutdown();

                closed.Should().BeTrue();
                state.Status.Should().Be(PrerequisiteStatus.Failed);
            });
    }

    private static async Task<PrerequisiteProcessState> CreateFailedStateAsync(
        JoinableTaskContext context,
        params PrerequisiteFailure[] failures)
    {
        var state = new PrerequisiteProcessState(context.Factory);
        var result = PrerequisiteResult.Failed(failures);
        await state.GetOrEvaluateAsync(_ => Task.FromResult(result), default);
        return state;
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static Task RunOnStaThreadAsync(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(
            () =>
            {
                try
                {
                    action();
                    completion.SetResult(true);
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override bool IsDisposed => false;

        public override Visual RootVisual { get; set; }

        protected override CompositionTarget GetCompositionTargetCore()
        {
            return null;
        }
    }
}
