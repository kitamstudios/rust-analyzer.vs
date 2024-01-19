using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

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
        envBlock.ToNullSeparatedDictionary().Should().BeEquivalentTo(expectedDict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\0")]
    [InlineData("abc=x\0\0")]
    [InlineData("A=x\u0001\0\0")]
    [InlineData("a=x\0abc=x\0\0")]
    [InlineData("A B=this\" is a\"b\0XX=this is xx\0A=a\0\0")]
    public void EnvBlockToEnvDictRoundTripTests(string envBlock)
    {
        envBlock
            .ToNullSeparatedDictionary()
            .ToEnvironmentBlock()
            .Should()
            .BeEquivalentTo(envBlock ?? "\0");
    }

    [Fact]
    public void EnvDictToEnvBlockRoundTripTests()
    {
        var procEnv = EnvironmentExtensions.GetEnvironmentVariables();

        procEnv
            .ToEnvironmentBlock()
            .ToNullSeparatedDictionary()
            .Should()
            .BeEquivalentTo(procEnv);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\0")]
    [InlineData("OS=macOS\0\0")]
    [InlineData("SYSTEMRoot=???\0abc=x\0\0")]
    [InlineData("USERDOMAIN=this\" is a\"b\0ProgramFiles(x86)=this is xx\0A=a\0\0")]
    public void OverrideWithEnvironmentBlockTests(string envBlock)
    {
        var newEnv = envBlock.OverrideProcessEnvironment();
        var envBlockDict = envBlock.ToNullSeparatedDictionary();

        newEnv.Should().HaveCountGreaterThanOrEqualTo(Environment.GetEnvironmentVariables().Count);
        newEnv.Should().Contain(new KeyValuePair<string, string>("windir", Environment.GetEnvironmentVariable("windir")));
        if (envBlockDict.Count() > 0)
        {
            newEnv.Should().Contain(envBlockDict);
        }
    }
}