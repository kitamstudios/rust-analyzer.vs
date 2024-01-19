using System;
using System.ComponentModel.Composition;
using System.IO;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace KS.RustAnalyzer.Infrastructure;

public interface IRegistrySettingsService
{
    public bool InfoBarDismissedByUser { get; set; }

    bool GetPackageRegistryRoot(out string packageRegistryRoot);
}

[Export(typeof(IRegistrySettingsService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class RegistrySettingsService : IRegistrySettingsService
{
    private const string DismissedRegKeyName = "release_notes_dismissed";

    private readonly TL _tl;

    private readonly IServiceProvider _serviceProvider;

    [ImportingConstructor]
    public RegistrySettingsService([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _serviceProvider = serviceProvider;
        _tl = new TL
        {
            T = t,
            L = l,
        };
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
