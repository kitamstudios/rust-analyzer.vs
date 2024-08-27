using System.Collections.Generic;
using System.Linq;

namespace KS.RustAnalyzer.UnitTests.Editor;

public static class TestData
{
    public static IEnumerable<object[]> Get()
    {
        return new[]
        {
            new[] { @"hello_library", @"Cargo.toml" },
            new[] { @"hello_library", @"src\lib.rs" },
            new[] { @"hello_world", @"Cargo.toml" },
            new[] { @"hello_world", @"src\main.rs" },
            new[] { @"hello_workspace", @"Cargo.toml" },
            new[] { @"hello_workspace", @"main\Cargo.toml" },
            new[] { @"hello_workspace", @"main\src\main.rs" },
            new[] { @"hello_workspace", @"shared\Cargo.toml" },
            new[] { @"hello_workspace", @"shared\src\lib.rs" },
            new[] { @"hello_workspace2", @"Cargo.toml" },
            new[] { @"workspace_mixed", @"Cargo.toml" },
            new[] { @"workspace_mixed", @"src\libx.rs" },
            new[] { @"workspace_mixed", @"src\main.rs" },
            new[] { @"workspace_mixed", @"shared\Cargo.toml" },
            new[] { @"workspace_mixed", @"shared\src\lib.rs" },
            /* Examples support */
            new[] { @"workspace_with_example", @"main\src\main.rs" },
            new[] { @"workspace_with_example", @"lib\examples\eg1.rs" },
            new[] { @"workspace_with_example", @"lib\examples\eg2\main.rs" },
            new[] { @"workspace_with_example", @"lib\examples\eg2\utils.rs" },
        }.AsEnumerable();
    }
}
