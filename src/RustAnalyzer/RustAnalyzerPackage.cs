using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Common;
using KS.RustAnalyzer.VS;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;

namespace KS.RustAnalyzer;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuids.RustAnalyzerString)]
public sealed class RustAnalyzerPackage : ToolkitPackage
{
    private ILogger _logger;

    private ITelemetryService _telemetry;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var cmServiceProvider = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
        _logger = cmServiceProvider?.GetService<ILogger>();
        _telemetry = new TelemetryServiceFactory().CreateService(null) as ITelemetryService;
    }

    protected override async Task OnAfterPackageLoadedAsync(CancellationToken cancellationToken)
    {
        await base.OnAfterPackageLoadedAsync(cancellationToken);

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await SearchAndDisableIncompatibleExtensionsAsync();
    }

    private async Task SearchAndDisableIncompatibleExtensionsAsync()
    {
        _logger?.WriteLine("Searching and disabling incompatible extensions.");

        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var exMgrAssembly = Assembly.LoadWithPartialName("Microsoft.VisualStudio.ExtensionManager");
#pragma warning restore CS0618 // Type or member is obsolete
            var exMgrType = exMgrAssembly.GetType("Microsoft.VisualStudio.ExtensionManager.SVsExtensionManager");
            dynamic exMgr = GetGlobalService(exMgrType);

            var enabledExtensions = exMgr.GetEnabledExtensions() as IEnumerable<dynamic>;
            var incompatibleExtensions = enabledExtensions
                .Where(x => (x.Header.Name as string).Equals("VS_RustAnalyzer", StringComparison.OrdinalIgnoreCase));
            if (incompatibleExtensions.Any())
            {
                var mbRet = await new MessageBox()
                    .ShowAsync(
                        $"{Vsix.Name} has detected incompatible extensions",
                        "Disable them and restart Visual Studio? You can enable them back later from Extensions > Manage Extensions.");
                if (mbRet == VSConstants.MessageBoxResult.IDOK)
                {
                    _telemetry?.TrackEvent("DisableIncompatExts", ("NumberOfExts", incompatibleExtensions.Count().ToString(CultureInfo.InvariantCulture)));
                    foreach (var e in incompatibleExtensions)
                    {
                        exMgr.Disable(e);
                    }
                }
                else
                {
                    _telemetry?.TrackEvent("DisableThisExt");
                    var thisExtension = enabledExtensions.Where(e => (e.Header.Name as string).Equals(Vsix.Name, StringComparison.OrdinalIgnoreCase));
                    exMgr.Disable(thisExtension);
                }

                await RestartProcessAsync();
            }
        }
        catch (Exception e)
        {
            _logger?.WriteLine("Failed in searching and disabling incompatible extensions. Ex: {0}", e);
        }
    }

    private async Task RestartProcessAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

        var args = Environment.GetCommandLineArgs();
        Process.Start(args[0], string.Join(" ", args.Skip(1)));

        await Task.Delay(2000);

        (GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE).Quit();
    }
}
