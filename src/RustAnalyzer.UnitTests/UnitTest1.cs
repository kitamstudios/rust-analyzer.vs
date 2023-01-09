using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KS.RustAnalyzer.UnitTests;

public class UnitTest1
{
    [Fact]
    public async Task Test1Async()
    {
        var val = await Task.FromResult(string.Empty);

        val.Any().Should().BeTrue();
    }
}
