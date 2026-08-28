using System;
using System.ComponentModel.Composition;
using EnsureThat;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.PythonTools.Editor;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace KS.RustAnalyzer.Editor;

/// <summary>
/// Stolen and adapted from https://github.com/microsoft/PTVS/blob/397135acd55be0fb17cfa206b893211150399052/Python/Product/PythonTools/PythonTools/Editor/Comment/PythonCommentSelectionCommandHandler.cs.
/// </summary>
[Export(typeof(ICommandHandler))]
[ContentType(Constants.RustLanguageContentType)]
[Name(nameof(CommentSelectionCommandHandler))]
public class CommentSelectionCommandHandler : ICommandHandler<CommentSelectionCommandArgs>, ICommandHandler<UncommentSelectionCommandArgs>
{
    private readonly Func<ITextView, bool, bool> _changeComment;
    private readonly PrerequisiteProcessState _prerequisiteState;
    private readonly TL _tl;

    [ImportingConstructor]
    public CommentSelectionCommandHandler([Import] ITelemetryService t, [Import] ILogger l)
        : this(
            t,
            l,
            PrerequisiteProcessState.Current,
            CommentHelper.CommentOrUncommentBlock)
    {
    }

    protected CommentSelectionCommandHandler(
        ITelemetryService t,
        ILogger l,
        PrerequisiteProcessState prerequisiteState,
        Func<ITextView, bool, bool> changeComment)
    {
        _prerequisiteState = EnsureArg.IsNotNull(
            prerequisiteState,
            nameof(prerequisiteState),
            options => options.WithException(
                new ArgumentNullException(nameof(prerequisiteState))));
        _changeComment = EnsureArg.IsNotNull(
            changeComment,
            nameof(changeComment),
            options => options.WithException(
                new ArgumentNullException(nameof(changeComment))));
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public string DisplayName => nameof(CommentSelectionCommandHandler);

    public CommandState GetCommandState(CommentSelectionCommandArgs args)
        => _prerequisiteState.IsAvailable
            ? CommandState.Available
            : CommandState.Unavailable;

    public CommandState GetCommandState(UncommentSelectionCommandArgs args)
        => _prerequisiteState.IsAvailable
            ? CommandState.Available
            : CommandState.Unavailable;

    public bool ExecuteCommand(CommentSelectionCommandArgs args, CommandExecutionContext executionContext)
    {
        if (!_prerequisiteState.IsAvailable)
        {
            return false;
        }

        try
        {
            _tl.T.TrackEvent("CommentSelection");
            return _changeComment(args.TextView, true);
        }
        catch (Exception e)
        {
            _tl.T.TrackException(e);
            throw;
        }
    }

    public bool ExecuteCommand(UncommentSelectionCommandArgs args, CommandExecutionContext executionContext)
    {
        if (!_prerequisiteState.IsAvailable)
        {
            return false;
        }

        try
        {
            _tl.T.TrackEvent("UncommentSelection");
            return _changeComment(args.TextView, false);
        }
        catch (Exception e)
        {
            _tl.T.TrackException(e);
            throw;
        }
    }
}
