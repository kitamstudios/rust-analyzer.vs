using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.Shell;

/// <summary>
/// This command will be shown even if a Rust project is not opened currently.
/// </summary>
[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdRustAnalyzerOptions)]
public sealed class OptionsCommand : BaseRustAnalyzerCommand<OptionsCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        Command.Visible = Command.Enabled = Command.Supported = true;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        Package.ShowOptionPage(typeof(OptionsProvider.GeneralOptions));

        await Task.CompletedTask;
    }
}

[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdRestartLSP)]
public sealed class RestartLspCommand : BaseRustAnalyzerCommand<RestartLspCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        await CommunityVS.MessageBox.ShowAsync("restart lsp command.");
    }
}

/// <summary>
/// This command will be shown even if a Rust project is not opened currently.
/// </summary>
[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdKillOrphaned)]
public sealed class KillOrphanedRaExesCommand : BaseRustAnalyzerCommand<KillOrphanedRaExesCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        Command.Visible = Command.Enabled = Command.Supported = true;
    }

    protected override Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        Constants.RAExeNameNoExtension
            .GetOrphanedProcesses()
            .ForEach(x => x.KillSafe());

        return Task.CompletedTask;
    }
}

[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdSwitchToolChain)]
public sealed class SwitchToolchainCommand : BaseRustAnalyzerCommand<SwitchToolchainCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        await CommunityVS.MessageBox.ShowAsync("switch toolchain command.");
    }
}
