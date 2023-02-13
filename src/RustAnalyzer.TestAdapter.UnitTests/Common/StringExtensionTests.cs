using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

public class StringExtensionTests
{
    [Theory]
    [InlineData(@"", "\0")]
    [InlineData(@"abc", "\0")]
    [InlineData(@"abc=", "\0")]
    [InlineData(@"abc=x=1", "\0")]
    [InlineData(@"abc=x", "abc=x\0\0")]
    [InlineData(@"a abc=x", "abc=x\0\0")]
    [InlineData(@"a= abc=x", "abc=x\0\0")]
    [InlineData(@"a=x=1 abc=x", "abc=x\0\0")]
    [InlineData(@"a=x abc=x", "a=x\0abc=x\0\0")]
    [InlineData(@" a=x   abc=x ", "a=x\0abc=x\0\0")]
    public void GetEnvironmentBlockTests(string str, string envBlock)
    {
        str.GetEnvironmentBlock().Should().Be(envBlock);
    }
}