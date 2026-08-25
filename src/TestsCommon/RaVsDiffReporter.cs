using ApprovalTests.Core;
using ApprovalTests.Reporters.TestFrameworks;

namespace KS.RustAnalyzer.Tests.Common;

public sealed class RaVsDiffReporter : IApprovalFailureReporter
{
    public static readonly IApprovalFailureReporter INSTANCE = XUnit2Reporter.INSTANCE;

    public bool IsWorkingInThisEnvironment(string forFile)
    {
        throw new System.NotImplementedException();
    }

    public void Report(string approved, string received)
    {
        throw new System.NotImplementedException();
    }
}
