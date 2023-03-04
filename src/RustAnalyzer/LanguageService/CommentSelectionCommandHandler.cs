using System;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.PythonTools.Editor;
using Microsoft.VisualStudio.Commanding;
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
    private readonly TL _tl;

    [ImportingConstructor]
    public CommentSelectionCommandHandler([Import] ITelemetryService t, [Import] ILogger l)
    {
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public string DisplayName => nameof(CommentSelectionCommandHandler);

    public CommandState GetCommandState(CommentSelectionCommandArgs args) => CommandState.Available;

    public CommandState GetCommandState(UncommentSelectionCommandArgs args) => CommandState.Available;

    public bool ExecuteCommand(CommentSelectionCommandArgs args, CommandExecutionContext executionContext)
    {
        try
        {
            _tl.T.TrackEvent("CommentSelection");
            return CommentHelper.CommentOrUncommentBlock(args.TextView, comment: true);
        }
        catch (Exception e)
        {
            _tl.T.TrackException(e);
            throw;
        }
    }

    public bool ExecuteCommand(UncommentSelectionCommandArgs args, CommandExecutionContext executionContext)
    {
        try
        {
            _tl.T.TrackEvent("UncommentSelection");
            return CommentHelper.CommentOrUncommentBlock(args.TextView, comment: false);
        }
        catch (Exception e)
        {
            _tl.T.TrackException(e);
            throw;
        }
    }
}
