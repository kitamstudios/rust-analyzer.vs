namespace KS.RustAnalyzer.Common;

/// <summary>
/// Stolen from https://github.com/microsoft/nodejstools/blob/main/Common/Product/SharedProject/ProcessOutput.cs.
/// </summary>
public abstract class ProcessOutputRedirector
{
    /// <summary>
    /// Called when a line is written to standard output.
    /// </summary>
    /// <param name="line">The line of text, not including the newline. This
    /// is never null.</param>
    public abstract void WriteLine(string line);

    public abstract void WriteLineWithoutProcessing(string line);

    /// <summary>
    /// Called when a line is written to standard error.
    /// </summary>
    /// <param name="line">The line of text, not including the newline. This
    /// is never null.</param>
    public abstract void WriteErrorLine(string line);

    public abstract void WriteErrorLineWithoutProcessing(string line);

    /// <summary>
    /// Called when output is written that should be brought to the user's
    /// attention. The default implementation does nothing.
    /// </summary>
    public virtual void Show()
    {
    }

    /// <summary>
    /// Called when output is written that should be brought to the user's
    /// immediate attention. The default implementation does nothing.
    /// </summary>
    public virtual void ShowAndActivate()
    {
    }

    /// <summary>
    /// Called to determine if stdin should be closed for a redirected process.
    /// The default is true.
    /// </summary>
    /// <returns>determine if stdin should be closed for a redirected process.</returns>
    public virtual bool CloseStandardInput()
    {
        return true;
    }
}
