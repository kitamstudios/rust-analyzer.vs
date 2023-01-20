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
using Microsoft.NET.StringTools;
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

            var allExtensions = exMgr.GetInstalledExtensions() as IEnumerable<dynamic>;
            var allExtensionIds = allExtensions.ToDictionary(x => x.Identifier as string);
            var incompatibleExtensions = AreIncompatibleExtensionsInstalled(allExtensionIds);
            if (incompatibleExtensions.Count != 0)
            {
                var mbRet = await new MessageBox()
                    .ShowAsync(
                        $"{Vsix.Name} has detected the followiing incompatible extensions:\r\n\r\n{string.Join("\r\n", incompatibleExtensions.Select(x => x.Id))}",
                        $"- OK: Disable the above and restart VS. (You can enable them back later from Extensions > Manage Extensions.)\r\n- Cancel: Disable {Vsix.Name} and restart VS.");
                if (mbRet == VSConstants.MessageBoxResult.IDOK)
                {
                    _telemetry?.TrackEvent("DisableIncompatExts", ("NumberOfExts", incompatibleExtensions.Count().ToString(CultureInfo.InvariantCulture)));
                    foreach (var e in incompatibleExtensions)
                    {
                        exMgr.Disable(e.Extension);
                    }
                }
                else
                {
                    _telemetry?.TrackEvent("DisableThisExt");
                    var thisExtension = allExtensionIds[Vsix.Id];
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

    private static IReadOnlyList<(string Id, dynamic Extension)> AreIncompatibleExtensionsInstalled(IDictionary<string, dynamic> allExtensions)
    {
        var incompatibleExtensions = new[]
        {
            "SourceGear.Rust.0c9f177a-b25e-4f25-9a35-b9049b4f9c9c",
            "VS_RustAnalyzer.c5a2b628-2a68-4643-808e-0838e3fb240b",
        };

        var installedIncompatibleExtensions = incompatibleExtensions
            .Aggregate(
                new List<(string, dynamic)>(),
                (acc, e) =>
                {
                    if (allExtensions.ContainsKey(e) && allExtensions[e].State.ToString() != "Disabled")
                    {
                        acc.Add((e, allExtensions[e]));
                    }

                    return acc;
                });

        return installedIncompatibleExtensions;
    }
}
