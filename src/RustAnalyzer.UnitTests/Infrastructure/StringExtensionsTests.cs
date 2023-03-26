using FluentAssertions;
using KS.RustAnalyzer.Infrastructure;
using Xunit;

namespace KS.RustAnalyzer.UnitTests.Infrastructure;

public class StringExtensionsTests
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
    [InlineData("\"A B=this\"\" is a\"\"b\" \"XX=this is xx\" A=a", "A B=this\" is a\"b\0XX=this is xx\0A=a\0\0")]
    public void GetEnvironmentBlockTests(string str, string envBlock)
    {
        str.GetEnvironmentBlock().Should().Be(envBlock);
    }

    [Theory]
    [InlineData(@"", "")]
    [InlineData(@"--config ""build.rustflags = '--cfg foo=bar'""", "--config\0build.rustflags = '--cfg foo=bar'")]
    public void ToNullSeparatedArrayTests(string str, string nullSepParts)
    {
        str.ToNullSeparatedArray().Should().Be(nullSepParts);
    }
}