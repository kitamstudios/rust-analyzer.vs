using System;

namespace KS.RustAnalyzer.Infrastructure;

public static class PredefinedCmdGuid
{
    public const string GuidWorkspaceExplorerBuildActionCmdSetString = "16537f6e-cb14-44da-b087-d1387ce3bf57";
    public static readonly Guid GuidWorkspaceExplorerBuildActionCmdSet = new (GuidWorkspaceExplorerBuildActionCmdSetString);
}

public static class PredefinedCmdId
{
    public const uint CmdIdBuildActionContext = 0x1000;
    public const uint CmdIdRebuildActionContext = 0x1010;
    public const uint CmdIdCleanActionContext = 0x1020;
}
