using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

public class StringBuildMessagePreprocessorTests
{
    private readonly StringBuildMessagePreprocessor _preprocessor = new ();

    [Theory]
    [InlineData(@"[31m-    let model_a2 = ModelA2 {", new[] { "-    let model_a2 = ModelA2 {" })]
    [InlineData(@"[0m[31m-        value: 20,", new[] { "-        value: 20," })]
    [InlineData(@"Diff in \\?\D:\src\ks\\main.rs at line 25:", new[] { @"D:\src\ks\\main.rs(25,1): warning: diffs created by fmt" })]
    [InlineData(@" --> src\main.rs:1:2", new[] { @"D:\src\ks\src\main.rs(1,2): error: clippy", @" --> src\main.rs:1:2" })]
    [InlineData(@"  --> main\src\main.rs:26:5", new[] { @"D:\src\ks\main\src\main.rs(26,5): error: clippy", @"  --> main\src\main.rs:26:5" })]
    public void GetEnvironmentBlockTests(string msg, string[] processedMsgs)
    {
        _preprocessor.Preprocess((PathEx)@"D:\src\ks", msg).Should().BeEquivalentTo(processedMsgs);
    }
}