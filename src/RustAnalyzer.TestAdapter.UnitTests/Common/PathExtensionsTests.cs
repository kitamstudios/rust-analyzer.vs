namespace KS.RustAnalyzer.TestAdapter.UnitTests.Common;

using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Common;
using Xunit;

public sealed class PathExtensionsTests
{
    [Theory]
    [InlineData(@"C:Documents", false)]
    [InlineData(@"\Documents", false)]
    [InlineData(@"/Documents", false)]
    [InlineData(@"C:/Documents", true)]
    [InlineData(@"C:\Documents\a.exe", true)]
    [InlineData(@"\\?\D:\src", true)]
    public void TestIsPathFullyQualified(string path, bool fullyQualified)
    {
        path.IsPathFullyQualified().Should().Be(fullyQualified);
    }

    [Theory]
    [InlineData(@"xxxxcmdxxxx_", null)]
    [InlineData(@"cmd.exe", @"c:\windows\system32\cmd.exe")]
    [InlineData(@"cMD.exE", @"C:\WindOws\SYSTEM32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe", @"C:\Windows\System32\cmd.exe")]
    [InlineData(@"explorer.exe", @"C:\Windows\explorer.exe")]
    public void TestFindInpath(string file, string path)
    {
        ((PathEx?)file.FindInPath()).Should().Be((PathEx?)path);
    }

    [Theory]
    [InlineData(@"c:\windows", @"c:\windows\system32\cmd.exe", @"system32\cmd.exe")]
    [InlineData(@"C:\WINdows", @"c:\windows\system32\CMD.exe", @"system32\cmd.exe")]
    [InlineData(@"C:\WINdows\system32", @"c:\windows\system32\CMD.exe", @"cmd.exe")]
    public void TestMakeRelativePath(string relativeTo, string path, string relativePath)
    {
        ((PathEx)relativeTo.MakeRelativePath(path)).Should().Be((PathEx)relativePath);
    }
}
