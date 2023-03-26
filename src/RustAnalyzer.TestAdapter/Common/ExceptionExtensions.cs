using System;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class ExceptionExtensions
{
    private const string ExitCodeKey = "ExitCode";
    private const int ExitCode101 = 101;

    public static T AddExitCode<T>(this T @this, int exitCode)
        where T : Exception
    {
        @this.Data["ExitCode"] = exitCode;
        return @this;
    }

    public static bool IsCargo101Error(this Exception @this)
    {
        return @this.Data.Contains(ExitCodeKey) && @this.Data[ExitCodeKey] is int exitCode && exitCode == ExitCode101;
    }
}
