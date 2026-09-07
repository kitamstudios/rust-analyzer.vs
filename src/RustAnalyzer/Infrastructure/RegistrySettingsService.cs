using System;
using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace KS.RustAnalyzer.Infrastructure;

public interface IRegistrySettingsService
{
    public bool InfoBarDismissedByUser { get; set; }

    string InstalledRustAnalyzerVersion { get; set; }

    bool GetPackageRegistryRoot(out string packageRegistryRoot);
}

[Export(typeof(IRegistrySettingsService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class RegistrySettingsService : IRegistrySettingsService
{
    private const string DismissedRegKeyName = "release_notes_dismissed";
    private const string InstalledRustAnalyzerVersionKey = "InstalledRlsVersion";

    private readonly IServiceProvider _serviceProvider;

    [ImportingConstructor]
    public RegistrySettingsService([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool InfoBarDismissedByUser
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetPackageRegistryRoot(out string regRoot))
            {
                return Registry.GetValue(regRoot, DismissedRegKeyName, null)?.ToString() == Vsix.Version;
            }

            return false;
        }

        set
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (value && GetPackageRegistryRoot(out string regRoot))
            {
                Registry.SetValue(regRoot, DismissedRegKeyName, Vsix.Version);
            }
        }
    }

    public string InstalledRustAnalyzerVersion
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetPackageRegistryRoot(out var regRoot)
                ? Registry.GetValue(regRoot, InstalledRustAnalyzerVersionKey, null) as string
                : null;
        }

        set
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!GetPackageRegistryRoot(out var regRoot))
            {
                throw new InvalidOperationException(
                    "The Visual Studio package registry root is unavailable.");
            }

            Registry.SetValue(regRoot, InstalledRustAnalyzerVersionKey, value);
        }
    }

    public bool GetPackageRegistryRoot(out string packageRegistryRoot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        packageRegistryRoot = null;
        if (_serviceProvider.GetService(typeof(SLocalRegistry)) is ILocalRegistry2 localReg && ErrorHandler.Succeeded(localReg.GetLocalRegistryRoot(out var localRegRoot)))
        {
            packageRegistryRoot = Path.Combine(Registry.CurrentUser.Name, localRegRoot, Vsix.Name);
            return true;
        }

        return false;
    }
}
