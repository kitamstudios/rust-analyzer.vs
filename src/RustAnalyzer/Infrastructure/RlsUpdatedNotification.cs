using System;
using System.Threading.Tasks;
using KS.RustAnalyzer;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

public static class RlsUpdatedNotification
{
    public const string RLSUPDATEDENVVARNAME = "RA.VS_RLS_UPDATED";

    private static int _counter = 0;

    public static bool Enabled
    {
        private get
        {
            return Environment.GetEnvironmentVariable(RLSUPDATEDENVVARNAME, EnvironmentVariableTarget.Process).IsNullOrEmpty();
        }

        set
        {
            Environment.SetEnvironmentVariable(RLSUPDATEDENVVARNAME, true.ToString(), EnvironmentVariableTarget.Process);
        }
    }

    public static async Task ShowAsync()
    {
        if (Enabled)
        {
            return;
        }

        if (IsTimeToShowAgain())
        {
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var model = new InfoBarModel(
            textSpans: new[] { new InfoBarTextSpan($"{Vsix.Name}: Rust Langugage Server was updated in the background. Restart VS to start using the new version. Using the old version may lead to degraded editor experience."), },
            Array.Empty<IVsInfoBarActionItem>(),
            image: KnownMonikers.StatusWarning,
            isCloseButtonVisible: true);
        var infoBar = await CommunityVS.InfoBar.CreateAsync(model);
        await infoBar.TryShowInfoBarUIAsync();
    }

    private static bool IsTimeToShowAgain()
    {
        return _counter++ % 10 != 0;
    }
}
