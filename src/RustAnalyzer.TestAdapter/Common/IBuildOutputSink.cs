using System;
using System.Threading.Tasks;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface IBuildOutputSink
{
    void Clear();

    void WriteLine(PathEx rootPath, Func<BuildMessage, Task> buildOutputTaskReporter, BuildMessage message);
}

public abstract class BuildMessage
{
}

public class StringBuildMessage : BuildMessage
{
    public string Message { get; set; }
}

public class DetailedBuildMessage : BuildMessage
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
