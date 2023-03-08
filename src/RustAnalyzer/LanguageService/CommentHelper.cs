using System.Diagnostics;
using System.Linq;
using KS.RustAnalyzer.TestAdapter;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Editor;

/// <summary>
/// Stolen and adapted from https://github.com/microsoft/PTVS/blob/397135acd55be0fb17cfa206b893211150399052/Python/Product/PythonTools/PythonTools/Editor/Comment/CommentHelper.cs.
/// </summary>
public static class CommentHelper
{
    public static bool CommentOrUncommentBlock(ITextView view, bool comment)
    {
        SnapshotPoint start, end;
        SnapshotPoint? mappedStart, mappedEnd;

        if (view.Selection.IsActive && !view.Selection.IsEmpty)
        {
            // comment every line in the selection
            start = view.Selection.Start.Position;
            end = view.Selection.End.Position;
            mappedStart = MapPoint(view, start);

            var endLine = end.GetContainingLine();
            if (endLine.Start == end)
            {
                // http://pytools.codeplex.com/workitem/814
                // User selected one extra line, but no text on that line.  So let's
                // back it up to the previous line.  It's impossible that we're on the
                // 1st line here because we have a selection, and we end at the start of
                // a line.  In normal selection this is only possible if we wrapped onto the
                // 2nd line, and it's impossible to have a box selection with a single line.
                end = end.Snapshot.GetLineFromLineNumber(endLine.LineNumber - 1).End;
            }

            mappedEnd = MapPoint(view, end);
        }
        else
        {
            // comment the current line
            start = end = view.Caret.Position.BufferPosition;
            mappedStart = mappedEnd = MapPoint(view, start);
        }

        if (mappedStart != null && mappedEnd != null &&
            mappedStart.Value <= mappedEnd.Value)
        {
            if (comment)
            {
                CommentRegion(view, mappedStart.Value, mappedEnd.Value);
            }
            else
            {
                UncommentRegion(view, mappedStart.Value, mappedEnd.Value);
            }

            // TODO: select multiple spans?
            // Select the full region we just commented, do not select if in projection buffer
            // (the selection might span non-language buffer regions)
            if (view.TextBuffer.IsPythonContent())
            {
                UpdateSelection(view, start, end);
            }

            return true;
        }

        return false;
    }

    private static bool IsPythonContent(this ITextBuffer buffer)
    {
        return buffer.ContentType.IsOfType(Constants.RustLanguageContentType);
    }

    private static bool IsPythonContent(this ITextSnapshot buffer)
    {
        return buffer.ContentType.IsOfType(Constants.RustLanguageContentType);
    }

    private static SnapshotPoint? MapPoint(ITextView view, SnapshotPoint point)
    {
        return view.BufferGraph.MapDownToFirstMatch(
           point,
           PointTrackingMode.Positive,
           IsPythonContent,
           PositionAffinity.Successor);
    }

    private static void CommentRegion(ITextView view, SnapshotPoint start, SnapshotPoint end)
    {
        Debug.Assert(start.Snapshot == end.Snapshot, "????");
        var snapshot = start.Snapshot;

        using (var edit = snapshot.TextBuffer.CreateEdit())
        {
            int minColumn = int.MaxValue;

            // first pass, determine the position to place the comment
            for (int i = start.GetContainingLine().LineNumber; i <= end.GetContainingLine().LineNumber; i++)
            {
                var curLine = snapshot.GetLineFromLineNumber(i);
                var text = curLine.GetText();

                int firstNonWhitespace = IndexOfNonWhitespaceCharacter(text);
                if (firstNonWhitespace >= 0 && firstNonWhitespace < minColumn)
                {
                    // ignore blank lines
                    minColumn = firstNonWhitespace;
                }
            }

            // second pass, place the comment
            for (int i = start.GetContainingLine().LineNumber; i <= end.GetContainingLine().LineNumber; i++)
            {
                var curLine = snapshot.GetLineFromLineNumber(i);
                if (string.IsNullOrWhiteSpace(curLine.GetText()))
                {
                    continue;
                }

                Debug.Assert(curLine.Length >= minColumn, "???");

                edit.Insert(curLine.Start.Position + minColumn, "//");
            }

            edit.Apply();
        }
    }

    private static int IndexOfNonWhitespaceCharacter(string text)
    {
        for (int j = 0; j < text.Length; j++)
        {
            if (!char.IsWhiteSpace(text[j]))
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>
    /// Removes a comment markers (//) from the start of each line.  If there is a selection the character is
    /// removed from each selected line.  Otherwise the character is removed from the current line.  Uncommented
    /// lines are ignored.
    /// </summary>
    private static void UncommentRegion(ITextView view, SnapshotPoint start, SnapshotPoint end)
    {
        Debug.Assert(start.Snapshot == end.Snapshot, "???");
        var snapshot = start.Snapshot;

        using (var edit = snapshot.TextBuffer.CreateEdit())
        {
            // first pass, determine the position to place the comment
            for (int i = start.GetContainingLine().LineNumber; i <= end.GetContainingLine().LineNumber; i++)
            {
                var curLine = snapshot.GetLineFromLineNumber(i);

                DeleteFirstCommentChar(edit, curLine);
            }

            edit.Apply();
        }
    }

    private static void UpdateSelection(ITextView view, SnapshotPoint start, SnapshotPoint end)
    {
        view.Selection.Select(
            new SnapshotSpan(
                start.GetContainingLine().Start.TranslateTo(view.TextBuffer.CurrentSnapshot, PointTrackingMode.Negative),
                end.GetContainingLine().End.TranslateTo(view.TextBuffer.CurrentSnapshot, PointTrackingMode.Positive)),
            false);
    }

    private static void DeleteFirstCommentChar(ITextEdit edit, ITextSnapshotLine curLine)
    {
        var text = curLine.GetText();
        for (int j = 0; j < text.Length; j++)
        {
            if (char.IsWhiteSpace(text[j]))
            {
                continue;
            }

            if (string.Concat(text.Skip(j).Take(2)) == "//")
            {
                edit.Delete(curLine.Start.Position + j, 2);
            }

            break;
        }
    }
}
