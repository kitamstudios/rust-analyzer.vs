using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace KS.RustAnalyzer.Shell;

public static class TemporaryTargetSystemStore
{
    public static string[] TargetSystems { get; set; } = { "Local Machine" };

    public static string CurrentTargetSystem { get; set; } = TargetSystems[0];
}

[Command(PackageGuids.guidRustAnalyzerExecutionTargetCmdSetString, PackageIds.ExecutionTargetCombo)]
public sealed class ExecutionTargetComboCommand : BaseRustAnalyzerCommand<ExecutionTargetComboCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        EnsureArg.IsNotNull(eventArgs);
        EnsureArg.IsTrue(eventArgs.InValue != default || eventArgs.OutValue != default);

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var input = eventArgs.InValue;
        var vOut = eventArgs.OutValue;

        // IDE is requesting the current value for the combo.
        if (vOut != IntPtr.Zero)
        {
            Marshal.GetNativeVariantForObject(TemporaryTargetSystemStore.CurrentTargetSystem, vOut);
            return;
        }

        // New value was selected in the combo.
        if (input != null)
        {
            TemporaryTargetSystemStore.CurrentTargetSystem = input.ToString();
        }
    }
}

[Command(PackageGuids.guidRustAnalyzerExecutionTargetCmdSetString, PackageIds.ExecutionTargetComboGetList)]
public sealed class ExecutionTargetComboGetListCommand : BaseCommand<ExecutionTargetComboGetListCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs eventArgs)
    {
        EnsureArg.IsNotNull(eventArgs);
        EnsureArg.IsNotDefault(eventArgs.OutValue);

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var vOut = eventArgs.OutValue;

        Marshal.GetNativeVariantForObject(TemporaryTargetSystemStore.TargetSystems, vOut);
    }
}
