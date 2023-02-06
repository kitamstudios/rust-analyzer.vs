using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
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
    public ISettingsService SettingsService { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    public void LaunchDebugTarget(IWorkspace workspaceContext, IServiceProvider serviceProvider, DebugLaunchActionContext debugLaunchActionContext)
    {
        workspaceContext.JTF.Run(async () => await LaunchDebugTargetAsync(workspaceContext, serviceProvider, debugLaunchActionContext));
    }

    public bool SupportsContext(IWorkspace workspaceContext, string targetFilePath)
    {
        var e = new NotImplementedException();

        L.WriteLine("SupportsContext should not have been called. This is unexpected.");
        T.TrackException(e);

        throw e;
    }

    private async Task LaunchDebugTargetAsync(IWorkspace workspaceContext, IServiceProvider serviceProvider, DebugLaunchActionContext debugLaunchActionContext)
    {
        try
        {
            var mds = workspaceContext.GetService<IMetadataService>();
            var package = await mds.GetContainingPackageAsync((PathEx)(debugLaunchActionContext.LaunchConfiguration[LaunchConfigurationConstants.ProgramKey] as string), default);
            var profile = debugLaunchActionContext.BuildConfiguration;
            var targetFQN = debugLaunchActionContext.LaunchConfiguration[LaunchConfigurationConstants.NameKey] as string;
            var target = package.GetTargets().FirstOrDefault(t => t.QualifiedTargetFileName == targetFQN);
            if (target == null)
            {
                string message = string.Format("Cannot find target '{0}' in '{1}', for profile '{2}'. This indicates a bug in the manifest parsing logic. Unable to start debugging.", targetFQN, package?.FullPath, profile);
                L.WriteError(message);
                T.TrackException(new ArgumentOutOfRangeException("target", message));
                await VsCommon.ShowMessageBoxAsync(message, "Try again after deleting the .vs folder. If that does not work please file a bug.");
                return;
            }

            L.WriteLine("LaunchDebugTarget with profile: {0}, launchConfiguration: {1}", profile, debugLaunchActionContext.LaunchConfiguration.SerializeObject());
            T.TrackEvent("Debug", ("Target", targetFQN), ("Profile", profile), ("Manifest", package.FullPath));

            var processName = target.GetPath(profile);
            if (!File.Exists(processName))
            {
                var message = string.Format("Unable to find file: '{0}'. This indicates a bug with the Manifest parsing logic. Unable to start debugging.", processName);
                L.WriteLine(message);
                T.TrackException(new FileNotFoundException(message, processName));
                await VsCommon.ShowMessageBoxAsync(message, "Try again after deleting the .vs folder. If that does not work please file a bug.");
                return;
            }

            string args = GetCommandLineArgs(workspaceContext.Location, debugLaunchActionContext);
            var noDebugFlag = debugLaunchActionContext.LaunchConfiguration.ContainsKey(LaunchConfigurationConstants.NoDebugKey) ? __VSDBGLAUNCHFLAGS.DBGLAUNCH_NoDebug : 0;
            var info = new VsDebugTargetInfo
            {
                dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                bstrExe = processName,
                bstrCurDir = Path.GetDirectoryName(processName),
                bstrArg = args,
                bstrEnv = null,
                bstrOptions = null,
                bstrPortName = null,
                bstrMdmRegisteredName = null,
                bstrRemoteMachine = null,
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<VsDebugTargetInfo>(),
                grfLaunch = (uint)(noDebugFlag | __VSDBGLAUNCHFLAGS.DBGLAUNCH_Silent | __VSDBGLAUNCHFLAGS.DBGLAUNCH_StopDebuggingOnEnd),
                fSendStdoutToOutputWindow = 0,
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

    private string GetCommandLineArgs(string location, DebugLaunchActionContext debugLaunchActionContext)
    {
        var projectKey = debugLaunchActionContext.LaunchConfiguration[LaunchConfigurationConstants.ProjectKey] as string;
        var relativePath = PathExtensions.MakeRelativePath(location, projectKey);
        return SettingsService.Get(VS.SettingsService.KindDebugger, VS.SettingsService.TypeCmdLineArgs, relativePath);
    }
}
