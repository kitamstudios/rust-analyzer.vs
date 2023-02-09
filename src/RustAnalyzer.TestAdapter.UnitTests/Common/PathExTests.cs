using System.Collections.Generic;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

public class PathExTests
{
    [Theory]
    [InlineData(@"abC", "Abc")]
    [InlineData(@"hello_library\Cargo.toml", "hello_library/Cargo.toml")]
    public void EqualityTests(string path, string equivalentPath)
    {
        PathEx p = (PathEx)path;
        PathEx eqP = (PathEx)equivalentPath;

        p.Should().Be(eqP);
        (p == eqP).Should().BeTrue();
        (p != eqP).Should().BeFalse();
        new Dictionary<PathEx, object> { [p] = null }.Should().ContainKey(eqP);
        new HashSet<PathEx> { p }.Should().Contain(eqP);
    }

    [Theory]
    [InlineData(@"abc", "abc")]
    [InlineData(@"abc/A", "abc\\A")]
    public void TostringTests(string path, string equivalentPath)
    {
        ((PathEx)path).ToString().Should().Be(equivalentPath);
    }
}