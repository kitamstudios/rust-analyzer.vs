namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Xunit;

public sealed class ToolchainServiceExtensionsTests
{
    [Fact]
    public async Task TestGetActiveToolChainAsync()
    {
        (await ToolchainServiceExtensions.GetDefaultToolchainAsync(TestHelpers.ThisTestRoot, default))
            .Should()
            .EndWith($"-{ToolchainServiceExtensions.AlwaysAvailableTarget}");
    }

    [Fact]
    public async Task TestGetBinAndLibPathsAsync()
    {
        var (binPath, libPath) = await ToolchainServiceExtensions.GetBinAndLibPathsAsync(TestHelpers.ThisTestRoot, default);

        Directory.EnumerateFiles(libPath, "std-*.*")
            .Select(x => Path.GetFileName(x).Remove(3, "-cef76c2685dfb4ca".Length))
            .Should()
            .BeEquivalentTo(new[] { "std.dll", "std.pdb", "std.dll.lib" });

        Directory.EnumerateFiles(binPath, "std-*.*")
            .Select(x => Path.GetFileName(x).Remove(3, "-cef76c2685dfb4ca".Length))
            .Should()
            .BeEquivalentTo(new[] { "std.dll", "std.pdb" });

        (binPath + "rustc.exe").FileExists().Should().BeTrue();
        (binPath + Constants.CargoExe).FileExists().Should().BeTrue();
    }

    [Fact]
    public async Task TestGetInstalledToolchainsBasicAsync()
    {
        var installToolchains = await ToolchainServiceExtensions.GetInstalledToolchainsAsync(TestHelpers.ThisTestRoot, default);

        installToolchains.Should().NotBeEmpty();
        installToolchains.Select(x => x.Name).Should().Contain(x => !x.IsNullOrEmptyOrWhiteSpace());
        installToolchains.Select(x => x.Version).Should().Contain(x => !x.IsNullOrEmptyOrWhiteSpace());
        installToolchains.Where(x => x.IsDefault).Should().HaveCount(1);
    }

    [Fact]
    public async Task TestGetTargetsAsync()
    {
        var targets = await ToolchainServiceExtensions.GetTargets(default);

        targets.Should().NotContain(ToolchainServiceExtensions.AlwaysAvailableTarget);
        targets.Should().OnlyContain(t => !t.Contains(" ("));
        targets.Take(ToolchainServiceExtensions.CommonTargets.Length)
            .Should()
            .ContainInOrder(ToolchainServiceExtensions.CommonTargets.OrderBy(x => x));
    }

    [Theory]
    [InlineData("/c echo success & echo error>&2", null, "success |finished", "error", true)]
    [InlineData("/c exit /b 1", null, "finished", null, false)]
    [InlineData("/c echo %cd% >&2", "WINDIR", "finished", null, true)]
    public async Task TestRunAsync(string args, string cwd, string eMessage, string eError, bool eRes)
    {
        var por = new TestPOR();
        var ret = await "cmd.exe".ToPath().RunAsync(args, (cwd ?? "USERPROFILE").GetEnvironmentValue().ToPath(), por, "finished", "cancelled", default);

        ret.Should().Be(eRes);
        por.Messages.Should().ContainInConsecutiveOrder(eMessage.Split('|'));
        var cwdErr = cwd == null ? null : new[] { $"{cwd.GetEnvironmentValue().ToLowerInvariant()} " };
        por.Errors.Select(x => x.ToLowerInvariant())
            .Should().ContainInConsecutiveOrder(eError?.Split('|') ?? cwdErr ?? Array.Empty<string>());
    }

    public sealed class TestPOR : ProcessOutputRedirector
    {
        private readonly ConcurrentQueue<string> _messages = new();

        private readonly ConcurrentQueue<string> _errors = new();

        public IEnumerable<string> Messages => _messages;

        public IEnumerable<string> Errors => _errors;

        public override void WriteErrorLine(string line)
        {
            _errors.Enqueue(line);
        }

        public override void WriteErrorLineWithoutProcessing(string line)
        {
            _errors.Enqueue(line);
        }

        public override void WriteLine(string line)
        {
            _messages.Enqueue(line);
        }

        public override void WriteLineWithoutProcessing(string line)
        {
            _messages.Enqueue(line);
        }
    }
}
