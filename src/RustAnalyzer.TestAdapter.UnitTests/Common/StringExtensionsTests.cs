using System.Linq;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

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
}
