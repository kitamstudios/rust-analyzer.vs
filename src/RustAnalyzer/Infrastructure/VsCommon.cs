using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.Infrastructure;

public static class VsCommon
{
    public static void ShowMessageBox(string line1)
    {
        ShowMessageBox(line1, string.Empty);
    }

    public static void ShowMessageBox(string line1, string line2)
    {
        CommunityVS.MessageBox.Show(
            line1.AddPrefixToMessage(),
            line2,
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK);
    }

    public static Task ShowMessageBoxAsync(string line1)
    {
        return ShowMessageBoxAsync(line1, string.Empty);
    }

    public static Task ShowMessageBoxAsync(string line1, string line2)
    {
        return CommunityVS.MessageBox.ShowAsync(
            line1.AddPrefixToMessage(),
            line2,
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK);
    }

    public static void Forget(this JoinableTask @this)
    {
    }

    private static string AddPrefixToMessage(this string @this) => $"[{Vsix.Name} v{Vsix.Version}]\n\n{@this}";
}
