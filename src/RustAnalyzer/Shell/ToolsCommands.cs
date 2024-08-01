using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.Shell;

// TODO: Logging and metrics for each of the commands.

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

    protected override void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs)
    {
        Package.ShowOptionPage(typeof(OptionsProvider.GeneralOptions));
    }
}

[Command(PackageGuids.guidRustAnalyzerToolsCmdSetString, PackageIds.IdRestartLSP)]
public sealed class RestartLspCommand : BaseRustAnalyzerCommand<RestartLspCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        Command.Visible = Command.Enabled = Command.Supported = false;
    }

    protected override void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs)
    {
        CommunityVS.MessageBox.Show("TODO: Restart lsp command.");
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

    protected override void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs)
    {
        Constants.RAExeNameNoExtension
            .GetOrphanedProcesses()
            .ForEach(x => x.KillSafe());
    }
}

[Command(PackageGuids.guidRustAnalyzerToolchainSwitcherString, PackageIds.IdFirstToolchain)]
public sealed class SwitchToolchainCommand : BaseRustAnalyzerCommand<SwitchToolchainCommand>
{
    private const string ToolchainNameProperty = "name";

    private static readonly List<OleMenuCommand> CommandCache = new ();

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var workspaceRoot = CmdServices.GetWorkspaceRoot();
        var toolchains = ThreadHelper.JoinableTaskFactory
            .Run(async () => await ToolChainServiceExtensions.GetInstalledToolchainsAsync(workspaceRoot, default));

        var mcs = Package.GetService<IMenuCommandService, OleMenuCommandService>();
        foreach (var (tc, pos) in toolchains.Select((x, i) => (x, i)))
        {
            var command = GetOrCreateCommand(pos, mcs);
            SetupCommand(command, tc);
        }
    }

    protected override void ExecuteCore(object sender, OleMenuCmdEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string workspaceRoot = null;
        if (ErrorHandler.Failed(Solution?.GetSolutionInfo(out workspaceRoot, out var _, out var _) ?? VSConstants.E_FAIL))
        {
            return;
        }

        var command = (OleMenuCommand)sender;
        if (!command.Properties.Contains(ToolchainNameProperty))
        {
            return;
        }

        var name = command.Properties[ToolchainNameProperty] as string;
        ThreadHelper.JoinableTaskFactory
            .RunAsync(async () => await ((PathEx)workspaceRoot).SetToolchainOverrideAsync(name, Logger, default))
            .FireAndForget();

        CommandCache.ForEach(c => c.Checked = false);
        command.Checked = true;
    }

    private OleMenuCommand GetOrCreateCommand(int pos, OleMenuCommandService mcs)
    {
        if (pos >= CommandCache.Count)
        {
            var command = CreateCommand(pos, mcs);
            CommandCache.Add(command);
        }

        var cmd = CommandCache[pos];
        cmd.Enabled = cmd.Supported = cmd.Visible = false;
        return cmd;
    }

    private OleMenuCommand CreateCommand(int pos, OleMenuCommandService mcs)
    {
        if (pos == 0)
        {
            return Command;
        }

        var cmdId = new CommandID(PackageGuids.guidRustAnalyzerToolchainSwitcher, PackageIds.IdFirstToolchain + pos);
        var command = new OleMenuCommand(Execute, cmdId);
        mcs.AddCommand(command);
        return command;
    }

    private void SetupCommand(OleMenuCommand command, Toolchain tc)
    {
        command.Enabled = command.Supported = command.Visible = true;
        command.Text = $"{tc.Name} [{tc.Version}]";
        command.Checked = tc.IsDefault;
        command.Properties[ToolchainNameProperty] = tc.Name;
    }
}
