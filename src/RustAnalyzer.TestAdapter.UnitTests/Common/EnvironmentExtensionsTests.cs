using System;
using System.Linq;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

// TODO: RELEASE: log VS version in OS version.
public class EnvironmentExtensionsTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("\0", "")]
    [InlineData("abc=x\0\0", "abc|x")]
    [InlineData("a=x\0abc=x\0\0", "a|x||abc|x")]
    [InlineData("A B=this\" is a\"b\0XX=this is xx\0A=a\0\0", "A B|this\" is a\"b||XX|this is xx||A|a")]
    public void GetDictionaryFromEnvironmentBlockTests(string envBlock, string dict)
    {
        var expectedDict = dict.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('|')).ToDictionary(x => x[0], x => x[1]);
        envBlock.GetDictionaryFromEnvironmentBlock().Should().BeEquivalentTo(expectedDict);
    }
}