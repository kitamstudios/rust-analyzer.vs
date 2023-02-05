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
        ((PathEx)path).Should().Be((PathEx)equivalentPath);
        ((PathEx)path == (PathEx)equivalentPath).Should().BeTrue();
        ((PathEx)path != (PathEx)equivalentPath).Should().BeFalse();
        ((PathEx)path).GetHashCode().Should().Be(path.GetHashCode());
    }

    [Theory]
    [InlineData(@"abc", "ABC")]
    [InlineData(@"abc/a", "ABC\\A")]
    public void TostringTests(string path, string equivalentPath)
    {
        ((PathEx)path).ToString().Should().Be(equivalentPath);
    }
}