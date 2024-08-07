using System;
using System.Runtime.InteropServices;
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

[Command(PackageGuids.guidRustAnalyzerTargetSystemCmdSetString, PackageIds.IdTargetSystemCombo)]
public sealed class TargetSystemComboCommand : BaseRustAnalyzerCommand<TargetSystemComboCommand>
{
    protected override void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs)
    {
        EnsureArg.IsNotNull(eventArgs);
        EnsureArg.IsTrue(eventArgs.InValue != default || eventArgs.OutValue != default);

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

[Command(PackageGuids.guidRustAnalyzerTargetSystemCmdSetString, PackageIds.IdTargetSystemComboGetList)]
public sealed class TargetSystemComboGetListCommand : BaseRustAnalyzerCommand<TargetSystemComboGetListCommand>
{
    protected override void ExecuteCore(object sender, OleMenuCmdEventArgs eventArgs)
    {
        EnsureArg.IsNotNull(eventArgs);
        EnsureArg.IsNotDefault(eventArgs.OutValue);

        var vOut = eventArgs.OutValue;

        Marshal.GetNativeVariantForObject(TemporaryTargetSystemStore.TargetSystems, vOut);
    }
}
