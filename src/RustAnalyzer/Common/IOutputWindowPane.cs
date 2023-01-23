using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.Common;

public interface IOutputWindowPane
{
    void Clear();

    void WriteLine(Func<BuildMessage, object, Task> buildMessageReporter, OutputMessage message);
}

public abstract class OutputMessage
{
}

public class StringOutputMessage : OutputMessage
{
    public string Message { get; set; }
}

public class BuildOutputMessage : OutputMessage
{
    public enum Level
    {
        None,
        Warning,
        Error,
    }

    public string Code { get; set; }

    public string ProjectFile { get; set; }

    public Level Type { get; set; }

    public string LogMessage { get; set; }

    public string TaskText { get; set; }

    public string File { get; set; }

    public int LineNumber { get; set; }

    public int ColumnNumber { get; set; }

    public string SubCategory { get; set; }

    public string HelpKeyword { get; set; }
}
