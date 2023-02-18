using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using static Microsoft.VisualStudio.VSConstants;
using RaSettingsService = KS.RustAnalyzer.Infrastructure.SettingsService;

namespace KS.RustAnalyzer.Debugger;

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
        var lcw = new LaunchConfigWrapper(debugLaunchActionContext.LaunchConfiguration, new TL { T = T, L = L, });
        workspaceContext.JTF.Run(async () => await LaunchDebugTargetAsync(workspaceContext, serviceProvider, debugLaunchActionContext.BuildConfiguration, lcw));
    }

    public bool SupportsContext(IWorkspace workspaceContext, string targetFilePath)
    {
        var e = new NotImplementedException();

        L.WriteLine("SupportsContext should not have been called. This is unexpected.");
        T.TrackException(e);

        throw e;
    }

    private async Task LaunchDebugTargetAsync(IWorkspace workspaceContext, IServiceProvider serviceProvider, string profile, LaunchConfigWrapper lcw)
    {
        try
        {
            var mds = workspaceContext.GetService<IMetadataService>();
            var package = await mds.GetContainingPackageAsync((PathEx)lcw[LaunchConfigurationConstants.ProgramKey], default);
            var targetFQN = lcw[LaunchConfigurationConstants.NameKey];
            var target = package.GetTargets().FirstOrDefault(t => t.QualifiedTargetFileName == targetFQN);
            if (target == null)
            {
                string message = string.Format("Cannot find target '{0}' in '{1}', for profile '{2}'. This indicates a bug in the manifest parsing logic. Unable to start debugging.", targetFQN, package?.FullPath, profile);
                L.WriteError(message);
                T.TrackException(new ArgumentOutOfRangeException("target", message));
                await VsCommon.ShowMessageBoxAsync(message, "Try again after deleting the .vs folder. If that does not work please file a bug.");
                return;
            }

            L.WriteLine("LaunchDebugTarget with profile: {0}, launchConfiguration: {1}", profile, lcw.SerializeObject());
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

            var args = GetSettings(RaSettingsService.TypeCommandLineArguments, workspaceContext.GetService<ISettingsService>(), lcw);
            var env = GetSettings(RaSettingsService.TypeDebuggerEnvironment, workspaceContext.GetService<ISettingsService>(), lcw);
            var noDebugFlag = lcw.ContainsKey(LaunchConfigurationConstants.NoDebugKey) ? __VSDBGLAUNCHFLAGS.DBGLAUNCH_NoDebug : 0;
            var info = new VsDebugTargetInfo
            {
                dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                bstrExe = processName,
                bstrCurDir = Path.GetDirectoryName(processName),
                bstrArg = args,
                bstrEnv = env.GetEnvironmentBlock(),
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
        catch (KeyNotFoundException knfe)
        {
            await VsCommon.ShowMessageBoxAsync(
                knfe.Message,
                "Debugger will not be launched. Please report the repro steps + this message as this issue is hard to track down. 🙏");
        }
        catch (Exception e)
        {
            T.TrackException(e);
            throw;
        }
    }

    private string GetSettings(string type, ISettingsService settingsService, LaunchConfigWrapper lcw)
    {
        var projectKey = lcw[LaunchConfigurationConstants.ProjectKey];
        return settingsService.Get(type, (PathEx)projectKey);
    }

    /// <summary>
    /// Wrapper to track a number of KeyNotFoundExceptions being thrown on users' machines.
    /// </summary>
    public sealed class LaunchConfigWrapper
    {
        private readonly IPropertySettings _lc;
        private readonly TL _tl;

        public LaunchConfigWrapper(IPropertySettings lc, TL tl)
        {
            _lc = lc;
            _tl = tl;
        }

        public string this[string key]
        {
            get
            {
                if (!_lc.ContainsKey(key) || _lc[key].GetType() != typeof(string))
                {
                    var msg = $"Key '{key}' is not set in launch configuration and / or is not a string.";
                    var e = new KeyNotFoundException(msg);
                    _tl.T.TrackException(e, new[] { ("Key", key) });
                    _tl.L.WriteError(msg);
                    throw e;
                }

                return _lc[key] as string;
            }
        }

        public bool ContainsKey(string noDebugKey) => _lc.ContainsKey(noDebugKey);
    }
}
