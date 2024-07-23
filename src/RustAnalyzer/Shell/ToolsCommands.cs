using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using EnvDTE;
using KS.RustAnalyzer.Infrastructure;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.Shell;

[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdRustAnalyzerOptions)]
public sealed class OptionsCommand : BaseRustAnalyzerCommand<OptionsCommand>
{
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

[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdKillOrphaned)]
public sealed class KillOrphanedRaExesCommand : BaseRustAnalyzerCommand<KillOrphanedRaExesCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        await CommunityVS.MessageBox.ShowAsync("kill orphaned command.");
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
