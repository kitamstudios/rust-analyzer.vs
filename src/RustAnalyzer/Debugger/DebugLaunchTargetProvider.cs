using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using static Microsoft.VisualStudio.VSConstants;

namespace KS.RustAnalyzer.Debugger;

// TODO: Workaround for https://github.com/kitamstudios/rust-analyzer.vs/issues/24. Just implementing LaunchDebugTargetProviderOptions.IsRuntimeSupportContext should be enough but it does not work, for now setting priority to low.
[ExportLaunchDebugTarget(LaunchDebugTargetProviderOptions.IsRuntimeSupportContext, ProviderType, new[] { ".exe" }, ProviderPriority.Lowest)]
public sealed class DebugLaunchTargetProvider : ILaunchDebugTargetProvider
{
    public const string ProviderType = "{72D3FCEF-1111-4266-B8DD-D3ED06E35A2B}";
    public static readonly Guid ProviderTypeGuid = new(ProviderType);

    [Import]
    public ILogger L { get; set; }

    [Import]
    public IFeatureUsageTelemetry UsageTelemetry { get; set; }

    [Import]
    public PrerequisiteAvailabilityPolicy AvailabilityPolicy { get; set; }

    public void LaunchDebugTarget(IWorkspace workspaceContext, IServiceProvider serviceProvider, DebugLaunchActionContext debugLaunchActionContext)
    {
        if (!AvailabilityPolicy.IsReady(AutomaticRustPath.DebugRunPreparation))
        {
            return;
        }

        var lcw = new LaunchConfigWrapper(debugLaunchActionContext.LaunchConfiguration, L);
        workspaceContext.JTF.Run(async () => await LaunchDebugTargetAsync(workspaceContext, serviceProvider, lcw, default));
    }

    public bool SupportsContext(IWorkspace workspaceContext, string targetFilePath)
    {
        if (!AvailabilityPolicy.IsReady(AutomaticRustPath.DebugRunPreparation))
        {
            return false;
        }

        var mds = workspaceContext.GetService<IMetadataService>();
        var package = workspaceContext.JTF.Run(async () => await workspaceContext.GetService<IMetadataService>()?.GetContainingPackageAsync((PathEx)targetFilePath, default));

        return package != null;
    }

    private async Task LaunchDebugTargetAsync(IWorkspace workspaceContext, IServiceProvider serviceProvider, LaunchConfigWrapper lcw, CancellationToken ct)
    {
        if (!AvailabilityPolicy.IsReady(AutomaticRustPath.DebugRunPreparation))
        {
            return;
        }

        var operation = lcw.ContainsKey(LaunchConfigurationConstants.NoDebugKey)
            ? UsageOperation.LaunchRun
            : UsageOperation.LaunchDebug;
        await TrackLaunchAsync(
            operation,
            () => LaunchDebugTargetCoreAsync(workspaceContext, serviceProvider, lcw, operation, ct));
    }

    private async Task<bool> LaunchDebugTargetCoreAsync(
        IWorkspace workspaceContext,
        IServiceProvider serviceProvider,
        LaunchConfigWrapper lcw,
        UsageOperation operation,
        CancellationToken ct)
    {
        const string diagMessage = "Delete the .vs folder and try again. If that does not work please file a bug with the repro steps.";
        try
        {
            var mds = workspaceContext.GetService<IMetadataService>();
            var package = await mds.GetContainingPackageAsync((PathEx)lcw[LaunchConfigurationConstants.ProgramKey], default);
            var profile = workspaceContext.GetProfile(package.ManifestPath);
            var targetFQN = lcw[LaunchConfigurationConstants.NameKey];
            var target = package.GetTargets().FirstOrDefault(t => t.QualifiedTargetFileName == targetFQN);
            if (target == null)
            {
                string message = string.Format("Cannot find target '{0}' in '{1}', for profile '{2}'.", targetFQN, package?.FullPath, profile);
                L.WriteError(message);
                await VsCommon.ShowMessageBoxAsync(message, diagMessage);
                return false;
            }

            var processName = target.GetPath(profile);
            if (!File.Exists(processName))
            {
                var message = string.Format("Unable to find file: '{0}'.", processName);
                L.WriteLine(message);
                await VsCommon.ShowMessageBoxAsync(message, diagMessage);
                return false;
            }

            var args = await GetSettingsAsync(SettingsInfo.TypeCommandLineArguments, workspaceContext.GetService<ISettingsService>(), lcw);
            var env = await GetSettingsAsync(SettingsInfo.TypeDebuggerEnvironment, workspaceContext.GetService<ISettingsService>(), lcw);
            var workingDirectory = await GetSettingsAsync(SettingsInfo.TypeDebuggerWorkingDirectory, workspaceContext.GetService<ISettingsService>(), lcw);
            var noDebugFlag = operation == UsageOperation.LaunchRun ? __VSDBGLAUNCHFLAGS.DBGLAUNCH_NoDebug : 0;

            L.WriteLine("LaunchDebugTarget with profile: {0}, launchConfiguration: {1}", profile, lcw.SerializeObject());

            var binLibPaths = await ToolchainServiceExtensions.GetBinAndLibPathsAsync(package.Parent.WorkspaceRoot, ct);
            var info = new VsDebugTargetInfo
            {
                dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                bstrExe = processName,
                bstrCurDir = workingDirectory.IsNullOrEmpty() ? Path.GetDirectoryName(processName) : workingDirectory,
                bstrArg = args,
                bstrEnv = env.OverrideProcessEnvironment()
                    .PrependToPathInEnviroment(
                        package.GetDepsPath(profile),
                        package.GetTargetPath(profile),
                        binLibPaths.Lib,
                        binLibPaths.Bin).ToEnvironmentBlock(),
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
            return true;
        }
        catch (KeyNotFoundException knfe)
        {
            await VsCommon.ShowMessageBoxAsync(
                knfe.Message,
                "Debugger will not be launched. Please report the repro steps + this message as this issue is hard to track down. 🙏");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            L.WriteError(
                "Operation '{0}' failed unexpectedly. Ex: {1}",
                "DebugLaunchTargetProvider.LaunchDebugTargetAsync",
                e);
            throw;
        }
    }

    private async Task TrackLaunchAsync(
        UsageOperation operation,
        Func<Task<bool>> launch)
    {
        var duration = Stopwatch.StartNew();
        try
        {
            var succeeded = await launch();
            UsageTelemetry.Track(
                operation,
                succeeded ? UsageOutcome.Succeeded : UsageOutcome.Failed,
                duration.Elapsed);
        }
        catch (OperationCanceledException)
        {
            UsageTelemetry.Track(operation, UsageOutcome.Cancelled, duration.Elapsed);
            throw;
        }
        catch (Exception)
        {
            UsageTelemetry.Track(operation, UsageOutcome.Failed, duration.Elapsed);
            throw;
        }
    }

    private Task<string> GetSettingsAsync(string type, ISettingsService settingsService, LaunchConfigWrapper lcw)
    {
        var projectKey = lcw[LaunchConfigurationConstants.ProjectKey];
        return settingsService.GetAsync(type, (PathEx)projectKey);
    }

    /// <summary>
    /// Wrapper to track a number of KeyNotFoundExceptions being thrown on users' machines.
    /// </summary>
    public sealed class LaunchConfigWrapper
    {
        private readonly IPropertySettings _lc;
        private readonly ILogger _logger;

        public LaunchConfigWrapper(IPropertySettings lc, ILogger logger)
        {
            _lc = lc;
            _logger = logger;
        }

        public string this[string key]
        {
            get
            {
                if (!_lc.ContainsKey(key) || _lc[key].GetType() != typeof(string))
                {
                    var msg = $"Key '{key}' is not set in launch configuration and / or is not a string.";
                    var e = new KeyNotFoundException(msg);
                    _logger.WriteError(msg);
                    throw e;
                }

                return _lc[key] as string;
            }
        }

        public bool ContainsKey(string noDebugKey) => _lc.ContainsKey(noDebugKey);
    }
}
