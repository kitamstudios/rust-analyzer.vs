using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteFailurePromptTests
{
    [Fact]
    public void ModelPresentsExactMappedTextAndEveryTypedFailureInDeterministicOrder()
    {
        var result = PrerequisiteResult.Failed(
            new[]
            {
                new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotOperational, "Cargo could not run."),
                new PrerequisiteFailure(PrerequisiteFailureKind.RustupNotOperational, "rustup could not run."),
                new PrerequisiteFailure(PrerequisiteFailureKind.UnsupportedVisualStudioHost, "Visual Studio is unsupported."),
                new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found."),
                new PrerequisiteFailure(PrerequisiteFailureKind.DefaultToolchainNotConfigured, "No default toolchain is configured."),
                new PrerequisiteFailure(PrerequisiteFailureKind.RustupNotFound, "rustup was not found."),
                new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo path was not found."),
            });

        var model = new PrerequisiteFailurePromptModel(result);

        model.Title.Should().Be("rust-analyzer.vs prerequisites failed");
        model.FailureMessages.Should().Equal(
            "Visual Studio is unsupported.",
            "rustup was not found.",
            "rustup could not run.",
            "No default toolchain is configured.",
            "Cargo path was not found.",
            "Cargo was not found.",
            "Cargo could not run.");
        model.Message.Should().Be(
            "rust-analyzer.vs cannot start because these prerequisites failed:\r\n\r\n" +
            "- Visual Studio is unsupported.\r\n" +
            "- rustup was not found.\r\n" +
            "- rustup could not run.\r\n" +
            "- No default toolchain is configured.\r\n" +
            "- Cargo path was not found.\r\n" +
            "- Cargo was not found.\r\n" +
            "- Cargo could not run.\r\n\r\n" +
            "Yes = Disable: Disable rust-analyzer.vs for this Visual Studio session and return control to Visual Studio. This does not open a browser, restart Visual Studio, or persist the choice.\r\n\r\n" +
            "No = Help: Open the prerequisite instructions in your system browser without changing prerequisite state, then show this message again.");
    }

    [Fact]
    public void ModelRejectsInvalidResults()
    {
        var untyped = PrerequisiteResult.Failed(
            new[] { new PrerequisiteFailure("legacy", "Legacy failure.") });

        Action createFromNull = () => new PrerequisiteFailurePromptModel(null);
        Action createFromSuccess = () => new PrerequisiteFailurePromptModel(PrerequisiteResult.Success);
        Action createFromUntypedFailure = () => new PrerequisiteFailurePromptModel(untyped);

        createFromNull.Should().Throw<ArgumentNullException>();
        createFromSuccess.Should().Throw<ArgumentException>();
        createFromUntypedFailure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ControllerRejectsInvalidStateAsync()
    {
        using var context = new JoinableTaskContext();
        var notEvaluated = new PrerequisiteProcessState(context.Factory);
        var ready = new PrerequisiteProcessState(context.Factory);
        await ready.GetOrEvaluateAsync(_ => Task.FromResult(PrerequisiteResult.Success), default);
        var suspended = new PrerequisiteProcessState(context.Factory);
        await suspended.GetOrEvaluateAsync(
            _ => Task.FromResult(
                PrerequisiteResult.Failed(
                    new[] { new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found.") })),
            default);
        suspended.Suspend();

        foreach (var state in new[] { notEvaluated, ready, suspended })
        {
            Action create = () => new PrerequisiteFailurePromptController(
                state,
                (_, _, _, _, _) => VSConstants.MessageBoxResult.IDYES,
                _ => { });

            create.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task ControllerRejectsUntypedFailedResultAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(
                PrerequisiteResult.Failed(
                    new[] { new PrerequisiteFailure("legacy", "Legacy failure.") })),
            default);

        Action create = () => new PrerequisiteFailurePromptController(
            state,
            (_, _, _, _, _) => VSConstants.MessageBoxResult.IDYES,
            _ => { });

        create.Should().Throw<ArgumentException>();
        state.Status.Should().Be(PrerequisiteStatus.Failed);
    }

    [Fact]
    public async Task YesUsesFrameworkYesNoWithYesDefaultAndSuspendsWithoutNavigationAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateFailedStateAsync(context);
        var openedUrls = new List<string>();
        var presentations = 0;
        string presentedMessage = null;
        var controller = new PrerequisiteFailurePromptController(
            state,
            (title, message, icon, buttons, defaultButton) =>
            {
                presentations++;
                presentedMessage = message;
                openedUrls.Should().BeEmpty();
                state.Status.Should().Be(PrerequisiteStatus.Failed);
                title.Should().Be("rust-analyzer.vs prerequisites failed");
                icon.Should().Be(OLEMSGICON.OLEMSGICON_WARNING);
                buttons.Should().Be(OLEMSGBUTTON.OLEMSGBUTTON_YESNO);
                defaultButton.Should().Be(OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return VSConstants.MessageBoxResult.IDYES;
            },
            openedUrls.Add);

        controller.Show();

        presentations.Should().Be(1);
        presentedMessage.Should().Be(controller.Model.Message);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().BeSameAs(result);
        openedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleNoResponsesNavigateExactlyAndPresentAgainBeforeYesAsync()
    {
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateFailedStateAsync(context);
        var responses = new Queue<VSConstants.MessageBoxResult>(
            new[]
            {
                VSConstants.MessageBoxResult.IDNO,
                VSConstants.MessageBoxResult.IDNO,
                VSConstants.MessageBoxResult.IDYES,
            });
        var openedUrls = new List<string>();
        var navigationCountsAtPresentation = new List<int>();
        var presentedMessages = new List<string>();
        var controller = new PrerequisiteFailurePromptController(
            state,
            (_, message, _, buttons, defaultButton) =>
            {
                navigationCountsAtPresentation.Add(openedUrls.Count);
                presentedMessages.Add(message);
                state.Status.Should().Be(PrerequisiteStatus.Failed);
                buttons.Should().Be(OLEMSGBUTTON.OLEMSGBUTTON_YESNO);
                defaultButton.Should().Be(OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return responses.Dequeue();
            },
            url =>
            {
                state.Status.Should().Be(PrerequisiteStatus.Failed);
                openedUrls.Add(url);
            });

        controller.Show();

        responses.Should().BeEmpty();
        navigationCountsAtPresentation.Should().Equal(0, 1, 2);
        presentedMessages.Should().HaveCount(3);
        presentedMessages.Distinct(StringComparer.Ordinal).Should().ContainSingle();
        openedUrls.Should().Equal(Constants.PrerequisitesUrl, Constants.PrerequisitesUrl);
        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().BeSameAs(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData((int)VSConstants.MessageBoxResult.IDCANCEL)]
    [InlineData((int)VSConstants.MessageBoxResult.IDABORT)]
    public async Task UnexpectedOrShutdownResultExitsFailedWithoutNavigationOrRedisplayAsync(int rawResult)
    {
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateFailedStateAsync(context);
        var openedUrls = new List<string>();
        var presentations = 0;
        var controller = new PrerequisiteFailurePromptController(
            state,
            (_, _, _, _, _) =>
            {
                presentations++;
                return (VSConstants.MessageBoxResult)rawResult;
            },
            openedUrls.Add);

        controller.Show();

        presentations.Should().Be(1);
        openedUrls.Should().BeEmpty();
        state.Status.Should().Be(PrerequisiteStatus.Failed);
        state.CachedResult.Should().BeSameAs(result);
    }

    private static async Task<(PrerequisiteProcessState State, PrerequisiteResult Result)> CreateFailedStateAsync(
        JoinableTaskContext context)
    {
        var result = PrerequisiteResult.Failed(
            new[] { new PrerequisiteFailure(PrerequisiteFailureKind.CargoNotFound, "Cargo was not found.") });
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(_ => Task.FromResult(result), default);
        return (state, result);
    }
}
