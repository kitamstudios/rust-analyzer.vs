namespace KS.RustAnalyzer.TestAdapter.UnitTests.Cargo;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Newtonsoft.Json;
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

    /// <summary>
    /// In cases of test failure:
    /// a. use "rustup show --verbose" to update the inline data first.
    /// b. 2 cases: one with same active and default toolchain, and another with separate active &amp; default toolchains.
    /// </summary>
    [Theory]
    [UseReporter(typeof(RaVsDiffReporter))]
    [InlineData(
#pragma warning disable SA1118 // Parameter should not span multiple lines
        "separate_active_and_default",
        @"Default host: x86_64-pc-windows-msvc
rustup home:  C:\Users\parth\scoop\persist\rustup\.rustup

installed toolchains
--------------------
stable-x86_64-pc-windows-msvc (active)
  rustc 1.86.0 (05f9846f8 2025-03-31)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\stable-x86_64-pc-windows-msvc

nightly-x86_64-pc-windows-msvc (default)
  rustc 1.89.0-nightly (8405332bd 2025-05-12)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\nightly-x86_64-pc-windows-msvc

nightly-2024-03-27-x86_64-pc-windows-msvc
  rustc 1.79.0-nightly (47ecded35 2024-03-26)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\nightly-2024-03-27-x86_64-pc-windows-msvc

1.75.0-x86_64-pc-windows-msvc
  rustc 1.75.0 (82e1608df 2023-12-21)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\1.75.0-x86_64-pc-windows-msvc

1.76.0-x86_64-pc-windows-msvc
  rustc 1.76.0 (07dca489a 2024-02-04)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\1.76.0-x86_64-pc-windows-msvc

active toolchain
----------------
name: stable-x86_64-pc-windows-msvc
active because: directory override for 'D:\'
compiler: rustc 1.86.0 (05f9846f8 2025-03-31)
path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\stable-x86_64-pc-windows-msvc
installed targets:
  aarch64-unknown-none
  wasm32-unknown-unknown
  x86_64-pc-windows-msvc")]
#pragma warning restore SA1118 // Parameter should not span multiple lines
    [InlineData(
#pragma warning disable SA1118 // Parameter should not span multiple lines
        "same_active_and_default",
        @"Default host: x86_64-pc-windows-msvc
rustup home:  C:\Users\parth\scoop\persist\rustup\.rustup

installed toolchains
--------------------
stable-x86_64-pc-windows-msvc
  rustc 1.86.0 (05f9846f8 2025-03-31)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\stable-x86_64-pc-windows-msvc

nightly-x86_64-pc-windows-msvc (active, default)
  rustc 1.89.0-nightly (8405332bd 2025-05-12)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\nightly-x86_64-pc-windows-msvc

nightly-2024-03-27-x86_64-pc-windows-msvc
  rustc 1.79.0-nightly (47ecded35 2024-03-26)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\nightly-2024-03-27-x86_64-pc-windows-msvc

1.75.0-x86_64-pc-windows-msvc
  rustc 1.75.0 (82e1608df 2023-12-21)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\1.75.0-x86_64-pc-windows-msvc

1.76.0-x86_64-pc-windows-msvc
  rustc 1.76.0 (07dca489a 2024-02-04)
  path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\1.76.0-x86_64-pc-windows-msvc

active toolchain
----------------
name: nightly-x86_64-pc-windows-msvc
active because: directory override for 'D:\src'
compiler: rustc 1.89.0-nightly (8405332bd 2025-05-12)
path: C:\Users\parth\scoop\persist\rustup\.rustup\toolchains\nightly-x86_64-pc-windows-msvc
installed targets:
  aarch64-unknown-none
  wasm32-unknown-unknown
  x86_64-pc-windows-msvc")]
#pragma warning restore SA1118 // Parameter should not span multiple lines
    public async Task TestGetInstalledToolchainsBasicAsync(string testName, string output)
    {
        NamerFactory.AdditionalInformation = testName;
        var installToolchains = await ToolchainServiceExtensions.GetInstalledToolchainsAsync(
            new ToolchainServiceExtensions.RustupShowOutput.Simulated(output.Split('\r', '\n')),
            TestHelpers.ThisTestRoot,
            default);

        Approvals.VerifyAll(installToolchains.OrderBy(x => x.Name).Select(o => o.SerializeObject(Formatting.Indented)), label: string.Empty);
    }

    [Fact]
    public async Task TestGetTargetsAsync()
    {
        var targets = await ToolchainServiceExtensions.GetTargets(default);

        targets.Should().NotContain(ToolchainServiceExtensions.AlwaysAvailableTarget);
        targets.Should().OnlyContain(t => !t.Contains("("));
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
