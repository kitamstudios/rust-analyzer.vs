using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.Infrastructure;

public sealed class PrerequisiteFailurePromptModel
{
    public PrerequisiteFailurePromptModel(PrerequisiteResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (result.IsSuccess)
        {
            throw new ArgumentException("A prerequisite failure prompt requires a failed result.", nameof(result));
        }

        if (result.Failures.Any(failure => failure.Kind == PrerequisiteFailureKind.Unclassified))
        {
            throw new ArgumentException("A prerequisite failure prompt requires typed failures.", nameof(result));
        }

        Title = $"{Vsix.Name} prerequisites failed";
        FailureMessages = result.Failures
            .OrderBy(failure => failure.Kind)
            .ThenBy(failure => failure.Message, StringComparer.Ordinal)
            .Select(failure => failure.Message)
            .ToImmutableArray();

        var heading = $"{Vsix.Name} cannot start because these prerequisites failed:";
        var failures = string.Join(
            "\r\n",
            FailureMessages.Select(message => $"- {message}"));
        var yesExplanation =
            $"Yes = Disable: Disable {Vsix.Name} for this Visual Studio session and return control to Visual Studio. This does not open a browser, restart Visual Studio, or persist the choice.";
        var noExplanation =
            "No = Help: Open the prerequisite instructions in your system browser without changing prerequisite state, then show this message again.";
        Message = $"{heading}\r\n\r\n{failures}\r\n\r\n{yesExplanation}\r\n\r\n{noExplanation}";
    }

    public string Title { get; }

    public string Message { get; }

    public ImmutableArray<string> FailureMessages { get; }
}

public sealed class PrerequisiteFailurePromptController
{
    private readonly PrerequisiteProcessState _state;
    private readonly Func<
        string,
        string,
        OLEMSGICON,
        OLEMSGBUTTON,
        OLEMSGDEFBUTTON,
        VSConstants.MessageBoxResult> _showMessageBox;

    private readonly Action<string> _openSystemBrowser;

    public PrerequisiteFailurePromptController(
        PrerequisiteProcessState state,
        Func<
            string,
            string,
            OLEMSGICON,
            OLEMSGBUTTON,
            OLEMSGDEFBUTTON,
            VSConstants.MessageBoxResult> showMessageBox,
        Action<string> openSystemBrowser)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _showMessageBox = showMessageBox ?? throw new ArgumentNullException(nameof(showMessageBox));
        _openSystemBrowser = openSystemBrowser ?? throw new ArgumentNullException(nameof(openSystemBrowser));

        if (_state.Status != PrerequisiteStatus.Failed ||
            _state.CachedResult == null ||
            _state.CachedResult.IsSuccess)
        {
            throw new InvalidOperationException(
                "The prerequisite failure prompt can be shown only for a failed prerequisite state.");
        }

        Model = new PrerequisiteFailurePromptModel(_state.CachedResult);
    }

    public PrerequisiteFailurePromptModel Model { get; }

    public static void ShowForFailedState(PrerequisiteProcessState state)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var controller = new PrerequisiteFailurePromptController(
            state,
            (title, message, icon, buttons, defaultButton) =>
                CommunityVS.MessageBox.Show(title, message, icon, buttons, defaultButton),
            VsShellUtilities.OpenSystemBrowser);
        controller.Show();
    }

    public void Show()
    {
        while (true)
        {
            var result = _showMessageBox(
                Model.Title,
                Model.Message,
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            if (result == VSConstants.MessageBoxResult.IDYES)
            {
                _state.Suspend();
                return;
            }

            if (result != VSConstants.MessageBoxResult.IDNO)
            {
                return;
            }

            _openSystemBrowser(Constants.PrerequisitesUrl);
        }
    }
}
