using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace KS.RustAnalyzer;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuids.RustAnalyzerString)]
public sealed class RustAnalyzerPackage : ToolkitPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
    }

    protected override async Task OnAfterPackageLoadedAsync(CancellationToken cancellationToken)
    {
        await base.OnAfterPackageLoadedAsync(cancellationToken);

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await DisableIncompatibleExtensionsAsync();
    }

    private async Task DisableIncompatibleExtensionsAsync()
    {
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
                    foreach (var e in incompatibleExtensions)
                    {
                        exMgr.Disable(e);
                    }
                }
                else
                {
                    var thisExtension = enabledExtensions.Where(e => (e.Header.Name as string).Equals(Vsix.Name, StringComparison.OrdinalIgnoreCase));
                    exMgr.Disable(thisExtension);
                }

                await RestartProcessAsync();
            }
        }
        catch
        {
            // TODO: Log the exception.
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
