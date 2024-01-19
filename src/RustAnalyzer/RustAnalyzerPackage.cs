using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideOptionPage(
    pageType: typeof(OptionsProvider.GeneralOptions),
    categoryName: Vsix.Name,
    pageName: "General",
    categoryResourceID: 0,
    pageNameResourceID: 0,
    supportsAutomation: false,
    SupportsProfiles = true,
    ProvidesLocalizedCategoryName = false)]
[Guid(PackageGuids.guidRustAnalyzerPackageString)]
public sealed class RustAnalyzerPackage : ToolkitPackage
{
    private TL _tl;
    private IRegistrySettingsService _regSettings;
    private IPreReqsCheckService _preReqs;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.RegisterCommandsAsync();

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var cmServiceProvider = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
        _tl = new TL
        {
            L = cmServiceProvider?.GetService<ILogger>(),
            T = cmServiceProvider?.GetService<ITelemetryService>(),
        };
        _regSettings = cmServiceProvider?.GetService<IRegistrySettingsService>();
        _preReqs = cmServiceProvider?.GetService<IPreReqsCheckService>();
    }

    protected override async Task OnAfterPackageLoadedAsync(CancellationToken cancellationToken)
    {
        await base.OnAfterPackageLoadedAsync(cancellationToken);

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await ReleaseSummaryNotification.ShowAsync(_regSettings, _tl);
        await SearchAndDisableIncompatibleExtensionsAsync();
        await _preReqs.SatisfyAsync();
    }

    #region Handling incompatible extensions

    private async Task SearchAndDisableIncompatibleExtensionsAsync()
    {
        _tl.L.WriteLine("Searching and disabling incompatible extensions.");

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
                var mbRet = await CommunityVS.MessageBox
                    .ShowAsync(
                        $"{Vsix.Name} has detected the followiing incompatible extensions:\r\n\r\n{string.Join("\r\n", incompatibleExtensions.Select(x => x.Id))}",
                        $"- OK: Disable the above and restart VS. (You can enable them back later from Extensions > Manage Extensions.)\r\n- Cancel: Disable {Vsix.Name} and restart VS.");
                if (mbRet == VSConstants.MessageBoxResult.IDOK)
                {
                    _tl.T.TrackEvent("DisableIncompatExts", ("Extensions", string.Join(",", incompatibleExtensions.Select(x => x.Id))));
                    foreach (var e in incompatibleExtensions)
                    {
                        exMgr.Disable(e.Extension);
                    }
                }
                else
                {
                    _tl.T.TrackEvent("DisableThisExt");
                    var thisExtension = allExtensionIds[Vsix.Id];
                    exMgr.Disable(thisExtension);
                }

                await CommunityVS.Shell.RestartAsync();
            }
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Failed in searching and disabling incompatible extensions. Ex: {0}", e);
            _tl.T.TrackException(e);
        }
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

    #endregion

    #region Release summary

    public static class ReleaseSummaryNotification
    {
        private const string ActionContextReleaseNotes = "release_notes";
        private const string ActionContextDismiss = "dismiss";
        private const string ActionContextGetHelp = "get_help";
        private const string ActionContextRateExtension = "rate_extension";
        private const string ActionContextTestExperienceDemo = "test_experience_demo";

        public static async Task ShowAsync(IRegistrySettingsService regSettings, TL tl)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            tl.L.WriteLine("Attempting to show release notes...");
            if (regSettings.InfoBarDismissedByUser)
            {
                tl.L.WriteLine("... Not showing release notes as it has already been dismissed by the user.");
                return;
            }

            var actionItems = new[]
            {
                new InfoBarHyperlink("Rate Extension", ActionContextRateExtension),
                new InfoBarHyperlink("Test experience demo", ActionContextTestExperienceDemo),
                new InfoBarHyperlink("Dismiss", ActionContextDismiss),
            };
            var model = new InfoBarModel(
                textSpans: new[] { new InfoBarTextSpan($"{Vsix.Name} updated: {Constants.ReleaseSummary}"), },
                actionItems,
                image: KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);
            var infoBar = await CommunityVS.InfoBar.CreateAsync(model);
            infoBar.ActionItemClicked += (s, ea) => InfoBar_ActionItemClicked(s, ea, regSettings, tl);
            await infoBar.TryShowInfoBarUIAsync();
        }

        private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e, IRegistrySettingsService regSettings, TL tl)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.ActionItem.ActionContext is not string actionContext)
            {
                return;
            }

            switch (actionContext)
            {
                case ActionContextRateExtension:
                    VsShellUtilities.OpenSystemBrowser(Constants.RateExtensionUrl);
                    break;

                case ActionContextTestExperienceDemo:
                    VsShellUtilities.OpenSystemBrowser(Constants.TestExperienceDemoUrl);
                    break;

                case ActionContextDismiss:
                    regSettings.InfoBarDismissedByUser = true;
                    (sender as InfoBar)?.Close();
                    break;

                default:
                    break;
            }

            tl.T.TrackEvent("InfoBarAction", ("Context", actionContext));
        }
    }

    #endregion
}
