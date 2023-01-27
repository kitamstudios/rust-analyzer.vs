using System;
using System.ComponentModel.Composition;
using System.Linq;
using AutoMapper;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using static Microsoft.VisualStudio.VSConstants;

namespace KS.RustAnalyzer.VS;

[ExportLaunchDebugTarget(ProviderType, new[] { ".exe" })]
public sealed class DebugLaunchTargetProvider : ILaunchDebugTargetProvider
{
    public const string ProviderType = "{72D3FCEF-1111-4266-B8DD-D3ED06E35A2B}";
    public static readonly Guid ProviderTypeGuid = new (ProviderType);

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    public void LaunchDebugTarget(IWorkspace workspaceContext, IServiceProvider serviceProvider, DebugLaunchActionContext debugLaunchActionContext)
    {
        try
        {
            var manifest = Manifest.Create(debugLaunchActionContext.LaunchConfiguration[LaunchConfigurationConstants.ProjectKey] as string);
            var profile = debugLaunchActionContext.BuildConfiguration;
            var targetFQN = debugLaunchActionContext.LaunchConfiguration[LaunchConfigurationConstants.ProjectTargetKey] as string;
            var target = manifest?.Targets?.FirstOrDefault(t => t.QualifiedTargetFileName == targetFQN);

            if (target == null)
            {
                string message = string.Format("Cannot find target {0} in {1}, for profile {2}. Unable to start debugging.", targetFQN, manifest?.FullPath, profile);
                L.WriteError(message);
                T.TrackException(new ArgumentOutOfRangeException("target", message));
                return;
            }

            L.WriteLine("LaunchDebugTarget with profile: {0}, launchConfiguration: {1}", profile, debugLaunchActionContext.LaunchConfiguration.SerializeObject());
            T.TrackEvent("Debug", ("Target", targetFQN), ("Profile", profile), ("Manifest", manifest.FullPath));

            var processName = target.GetPath(profile);
            var noDebugFlag = debugLaunchActionContext.LaunchConfiguration.ContainsKey(LaunchConfigurationConstants.NoDebugKey) ? __VSDBGLAUNCHFLAGS.DBGLAUNCH_NoDebug : 0;
            var info = new VsDebugTargetInfo
            {
                dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                bstrExe = processName,
                bstrCurDir = Path.GetDirectoryName(processName),
                bstrArg = null,
                bstrEnv = null,
                bstrOptions = null,
                bstrPortName = null,
                bstrMdmRegisteredName = null,
                bstrRemoteMachine = null,
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<VsDebugTargetInfo>(),
                grfLaunch = (uint)(noDebugFlag | __VSDBGLAUNCHFLAGS.DBGLAUNCH_Silent | __VSDBGLAUNCHFLAGS.DBGLAUNCH_StopDebuggingOnEnd),
                fSendStdoutToOutputWindow = 1,
                clsidCustom = DebugEnginesGuids.NativeOnly_guid,
            };

            VsShellUtilities.LaunchDebugger(serviceProvider, info);
        }
        catch (Exception e)
        {
            T.TrackException(e);
            throw;
        }
    }

    public bool SupportsContext(IWorkspace workspaceContext, string targetFilePath)
    {
        var e = new NotImplementedException();

        L.WriteLine("SupportsContext should not have been called. This is unexpected.");
        T.TrackException(e);

        throw new NotImplementedException();
    }
}
