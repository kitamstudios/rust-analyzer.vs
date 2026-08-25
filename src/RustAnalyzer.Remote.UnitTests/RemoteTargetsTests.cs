using FluentAssertions;
using Xunit;

namespace KS.RustAnalyzer.Remote.UnitTests;

[Trait("type", "UnitTests")]
public class RemoteTargetsTests
{
    [Fact]
    public void DummyTest()
    {
        false.Should().BeFalse();
    }
}
