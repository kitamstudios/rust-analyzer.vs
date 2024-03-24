using System;
using ApprovalTests.Core;
using ApprovalTests.Reporters;
using ApprovalTests.Reporters.TestFrameworks;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Tests.Common;

public sealed class RaVsDiffReporter : IApprovalFailureReporter
{
    public static readonly IApprovalFailureReporter INSTANCE =
        !Environment.GetEnvironmentVariable("CI").IsNullOrEmptyOrWhiteSpace()
        ? XUnit2Reporter.INSTANCE
        : DiffReporter.INSTANCE;

    public bool IsWorkingInThisEnvironment(string forFile)
    {
        throw new NotImplementedException();
    }

    public void Report(string approved, string received)
    {
        throw new NotImplementedException();
    }
}
