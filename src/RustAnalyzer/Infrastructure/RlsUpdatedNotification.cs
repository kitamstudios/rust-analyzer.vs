using System;
using System.Threading.Tasks;
using KS.RustAnalyzer;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

public static class RlsUpdatedNotification
{
    public const string RLSUPDATEDENVVARNAME = "RA.VS_RLS_UPDATED";
    private const string ActionContextRestartVS = "restart_vs";

    private static int _counter = 0;

    public static bool Enabled
    {
        private get
        {
            return Environment.GetEnvironmentVariable(RLSUPDATEDENVVARNAME, EnvironmentVariableTarget.Process).IsNotNullOrEmpty();
        }

        set
        {
            var val = value ? true.ToString() : null;
            Environment.SetEnvironmentVariable(RLSUPDATEDENVVARNAME, val, EnvironmentVariableTarget.Process);
        }
    }

    public static async Task ShowAsync()
    {
        if (!Enabled)
        {
            return;
        }

        if (!IsTimeToShowAgain())
        {
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var actionItems = new[]
        {
            new InfoBarHyperlink("Restart", ActionContextRestartVS),
        };
        var model = new InfoBarModel(
            textSpans: new[] { new InfoBarTextSpan($"{Vsix.Name}: Rust Langugage Server was updated in the background. Restart VS to start using the new version. Using the old version may lead to degraded editor experience."), },
            actionItems,
            image: KnownMonikers.StatusWarning,
            isCloseButtonVisible: true);
        var infoBar = await CommunityVS.InfoBar.CreateAsync(model);
        infoBar.ActionItemClicked += (s, ea) => InfoBar_ActionItemClicked(s, ea);
        await infoBar.TryShowInfoBarUIAsync();
    }

    private static void InfoBar_ActionItemClicked(object s, InfoBarActionItemEventArgs ea)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (ea.ActionItem.ActionContext is not string actionContext)
        {
            return;
        }

        switch (actionContext)
        {
            case ActionContextRestartVS:
                RustAnalyzerPackage.JTF.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    Enabled = false; // NOTE: Restart restores the environment variables.
                    await CommunityVS.Shell.RestartAsync();
                }).FireAndForget();

                break;

            default:
                break;
        }
    }

    private static bool IsTimeToShowAgain()
    {
        return _counter++ % 10 == 0;
    }
}
