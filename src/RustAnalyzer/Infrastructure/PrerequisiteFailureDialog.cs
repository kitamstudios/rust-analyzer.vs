using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.Infrastructure;

public sealed class PrerequisiteFailureDialogModel
{
    public PrerequisiteFailureDialogModel(PrerequisiteResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (result.IsSuccess)
        {
            throw new ArgumentException("A prerequisite failure dialog requires a failed result.", nameof(result));
        }

        if (result.Failures.Any(failure => failure.Kind == PrerequisiteFailureKind.Unclassified))
        {
            throw new ArgumentException("A prerequisite failure dialog requires typed failures.", nameof(result));
        }

        Title = $"{Vsix.Name} prerequisites failed";
        Heading = $"{Vsix.Name} cannot start because these prerequisites failed:";
        DisableLabel = "Disable";
        HelpLabel = "Help";
        DisableExplanation =
            $"Disable turns off {Vsix.Name} for this Visual Studio session, closes this dialog, and returns control to Visual Studio. Restart Visual Studio to recheck prerequisites.";
        HelpExplanation =
            "Help opens the prerequisite instructions in your system browser. This dialog stays open and prerequisite state does not change.";
        FailureMessages = result.Failures
            .OrderBy(failure => failure.Kind)
            .ThenBy(failure => failure.Message, StringComparer.Ordinal)
            .Select(failure => failure.Message)
            .ToImmutableArray();
    }

    public string Title { get; }

    public string Heading { get; }

    public string DisableLabel { get; }

    public string HelpLabel { get; }

    public string DisableExplanation { get; }

    public string HelpExplanation { get; }

    public ImmutableArray<string> FailureMessages { get; }
}

public sealed class PrerequisiteFailureDialogController
{
    private readonly PrerequisiteProcessState _state;
    private readonly Action<string> _openSystemBrowser;

    public PrerequisiteFailureDialogController(
        PrerequisiteProcessState state,
        Action<string> openSystemBrowser)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _openSystemBrowser = openSystemBrowser ?? throw new ArgumentNullException(nameof(openSystemBrowser));

        if (_state.Status != PrerequisiteStatus.Failed ||
            _state.CachedResult == null ||
            _state.CachedResult.IsSuccess)
        {
            throw new InvalidOperationException(
                "The prerequisite failure dialog can be shown only for a failed prerequisite state.");
        }

        Model = new PrerequisiteFailureDialogModel(_state.CachedResult);
    }

    public PrerequisiteFailureDialogModel Model { get; }

    public void Disable()
    {
        _state.Suspend();
    }

    public void Help()
    {
        _openSystemBrowser(Constants.PrerequisitesUrl);
    }
}

public sealed class PrerequisiteFailureDialog : DialogWindow
{
    private readonly PrerequisiteFailureDialogController _controller;
    private bool _allowClose;

    public PrerequisiteFailureDialog(PrerequisiteFailureDialogController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        Title = _controller.Model.Title;
        Width = 640;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        IsCloseButtonEnabled = false;
        DialogContent = CreateContent();
    }

    public FrameworkElement DialogContent { get; }

    public static void ShowForFailedState(PrerequisiteProcessState state)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var controller = new PrerequisiteFailureDialogController(
            state,
            url => VsShellUtilities.OpenSystemBrowser(url));
        var dialog = new PrerequisiteFailureDialog(controller);
        dialog.Content = dialog.DialogContent;
        var shellEvents = CommunityVS.Events.ShellEvents;
        shellEvents.ShutdownStarted += dialog.CloseForShutdown;
        try
        {
            dialog.ShowModal();
        }
        finally
        {
            shellEvents.ShutdownStarted -= dialog.CloseForShutdown;
        }
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = !_allowClose;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private Grid CreateContent()
    {
        var layout = new Grid
        {
            Margin = new Thickness(24),
        };
        for (var row = 0; row < 5; row++)
        {
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var heading = CreateTextBlock(_controller.Model.Heading);
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);

        var failures = new StackPanel();
        foreach (var message in _controller.Model.FailureMessages)
        {
            failures.Children.Add(
                CreateTextBlock(
                    $"\u2022 {message}",
                    new Thickness(0, 0, 0, 8)));
        }

        var failureList = new ScrollViewer
        {
            Content = failures,
            Margin = new Thickness(0, 12, 0, 4),
            MaxHeight = 320,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(failureList, 1);
        layout.Children.Add(failureList);

        var disableExplanation = CreateTextBlock(
            _controller.Model.DisableExplanation,
            new Thickness(0, 12, 0, 0));
        Grid.SetRow(disableExplanation, 2);
        layout.Children.Add(disableExplanation);

        var helpExplanation = CreateTextBlock(
            _controller.Model.HelpExplanation,
            new Thickness(0, 8, 0, 0));
        Grid.SetRow(helpExplanation, 3);
        layout.Children.Add(helpExplanation);

        var disableButton = new DialogButton
        {
            Content = _controller.Model.DisableLabel,
            IsDefault = true,
            MinWidth = 88,
        };
        disableButton.Click += DisableButtonClick;

        var helpButton = new DialogButton
        {
            Content = _controller.Model.HelpLabel,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 88,
        };
        helpButton.Click += HelpButtonClick;

        var actions = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
            Orientation = Orientation.Horizontal,
        };
        actions.Children.Add(disableButton);
        actions.Children.Add(helpButton);
        Grid.SetRow(actions, 4);
        layout.Children.Add(actions);

        return layout;
    }

    private static TextBlock CreateTextBlock(string text, Thickness margin = default)
    {
        return new TextBlock
        {
            Margin = margin,
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private void DisableButtonClick(object sender, RoutedEventArgs e)
    {
        _controller.Disable();
        _allowClose = true;
        Close();
    }

    private void HelpButtonClick(object sender, RoutedEventArgs e)
    {
        _controller.Help();
    }
}
