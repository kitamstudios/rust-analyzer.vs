using System;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace KS.RustAnalyzer.Shell;

/// <summary>
/// NOTE: Consider adding visiblity constraints https://github.com/madskristensen/VisibilityConstraintsSample
/// TODO: Hide all this if Rust is not the current project.
/// </summary>
public abstract class BaseRustAnalyzerCommand<T> : BaseCommand<T>
    where T : class, new()
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Command.Visible = Command.Enabled = Command.Supported = IsCommandActive();
    }

    protected virtual bool IsCommandActive()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // TODO: Ensure this is a rust workspace.
        return true;
    }
}
