using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

[Trait("type", "UnitTests")]
public sealed class PrerequisiteFailureDialogTests
{
    [Fact]
    public void ModelPresentsEveryTypedFailureInDeterministicOrder()
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
            });

        var model = new PrerequisiteFailureDialogModel(result);

        model.Title.Should().Be("rust-analyzer.vs prerequisites failed");
        model.Heading.Should().Be("rust-analyzer.vs cannot start because these prerequisites failed:");
        model.DisableLabel.Should().Be("Disable");
        model.HelpLabel.Should().Be("Help");
        model.DisableExplanation.Should().Be(
            "Disable turns off rust-analyzer.vs for this Visual Studio session, closes this dialog, and returns control to Visual Studio. Restart Visual Studio to recheck prerequisites.");
        model.HelpExplanation.Should().Be(
            "Help opens the prerequisite instructions in your system browser. This dialog stays open and prerequisite state does not change.");
        model.FailureMessages.Should().Equal(
            "Visual Studio is unsupported.",
            "rustup was not found.",
            "rustup could not run.",
            "No default toolchain is configured.",
            "Cargo was not found.",
            "Cargo could not run.");
    }

    [Fact]
    public void ModelRejectsSuccessfulOrUntypedResults()
    {
        var untyped = PrerequisiteResult.Failed(
            new[] { new PrerequisiteFailure("legacy", "Legacy failure.") });

        Action createFromSuccess = () => new PrerequisiteFailureDialogModel(PrerequisiteResult.Success);
        Action createFromUntypedFailure = () => new PrerequisiteFailureDialogModel(untyped);

        createFromSuccess.Should().Throw<ArgumentException>();
        createFromUntypedFailure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ControllerCannotBeCreatedForNonfailedStateAsync()
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
            Action create = () => new PrerequisiteFailureDialogController(state, _ => { });

            create.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task ControllerRejectsUntypedFailedStateAsync()
    {
        using var context = new JoinableTaskContext();
        var state = new PrerequisiteProcessState(context.Factory);
        await state.GetOrEvaluateAsync(
            _ => Task.FromResult(
                PrerequisiteResult.Failed(
                    new[] { new PrerequisiteFailure("legacy", "Legacy failure.") })),
            default);

        Action create = () => new PrerequisiteFailureDialogController(state, _ => { });

        create.Should().Throw<ArgumentException>();
        state.Status.Should().Be(PrerequisiteStatus.Failed);
    }

    [Fact]
    public async Task HelpNavigatesOnlyWhenInvokedAndDoesNotMutateStateAsync()
    {
        var openedUrls = new List<string>();
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateFailedStateAsync(context);

        var controller = new PrerequisiteFailureDialogController(state, openedUrls.Add);

        openedUrls.Should().BeEmpty();
        state.Status.Should().Be(PrerequisiteStatus.Failed);

        controller.Help();
        controller.Help();

        openedUrls.Should().Equal(Constants.PrerequisitesUrl, Constants.PrerequisitesUrl);
        state.Status.Should().Be(PrerequisiteStatus.Failed);
        state.CachedResult.Should().BeSameAs(result);
    }

    [Fact]
    public async Task DisableSuspendsOnceWithoutNavigatingAsync()
    {
        var openedUrls = new List<string>();
        using var context = new JoinableTaskContext();
        var (state, result) = await CreateFailedStateAsync(context);
        var controller = new PrerequisiteFailureDialogController(state, openedUrls.Add);

        controller.Disable();
        Action disableAgain = controller.Disable;

        state.Status.Should().Be(PrerequisiteStatus.Suspended);
        state.CachedResult.Should().BeSameAs(result);
        openedUrls.Should().BeEmpty();
        disableAgain.Should().Throw<InvalidOperationException>();
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
