using System.Linq;
using System.Text.RegularExpressions;
using ApprovalTests.Reporters.TestFrameworks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

[Trait("type", "UnitTests")]
public class StringExtensionsTests
{
    [Theory]
    [InlineData(new string[] { }, "", 5)]
    [InlineData(new string[] { "a", "b", "c" }, "a|b|c", 5)]
    [InlineData(new string[] { "a1", "b2", "c3" }, "a1#b2#c3", 3)]
    [InlineData(new string[] { "a1", "b2", "c3" }, "a1|b2#c3", 4)]
    [InlineData(new string[] { "a1", "b2", "c3" }, "a1|b2#c3", 5)]
    [InlineData(new string[] { "a1", "b2", "c3" }, "a1|b2|c3", 6)]
    [InlineData(new string[] { "a1", "b2", "c3", "d4", "e5", "f6" }, "a1|b2#c3|d4#e5|f6", 4)]
    public void PartitionBasedOnMaxCombinedLength(string[] strs, string outStrs, int maxLength)
    {
        var ret = strs.PartitionBasedOnMaxCombinedLength(maxLength);

        var x = string.Join("#", ret.Select(l => string.Join("|", l)));

        x.Should().Be(outStrs);
    }

    [Fact]
    public void RegexReplaceHonorsOptions()
    {
        "ABC".RegexReplace("abc", "x", RegexOptions.IgnoreCase).Should().Be("x");
    }

    [Fact]
    public void SerializeAndNormalizeObjectRemovesIncidentalTestOutput()
    {
        var value = new
        {
            StartTime = "start",
            EndTime = "end",
            Duration = "00:00:00.1234567",
            Error = "thread 'tests::case' (12345) panicked",
            Path = @"target\release\build\crate\0123456789abcdef\out\crate-0123456789abcdef.exe",
        };

        var normalized = value.SerializeAndNormalizeObject();

        normalized.Should().NotContain("StartTime").And.NotContain("EndTime");
        normalized.Should().Contain(@"""Duration"": ""00:00:00""");
        normalized.Should().Contain("thread 'tests::case' panicked");
        normalized.Should().Contain(@"crate\\*\\out\\crate-*.exe");
    }

    [Fact]
    public void ApprovalReporterIsAlwaysXUnit()
    {
        RaVsDiffReporter.INSTANCE.Should().BeSameAs(XUnit2Reporter.INSTANCE);
    }
}
