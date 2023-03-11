using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.NodeEnhancements;

// TODO: XPLAT: Add proper examples for cross compilation.
public class NodeBrowseObject : INotifyPropertyChanged
{
    private readonly IDictionary<string, string> _propertyValueStore = new Dictionary<string, string>();

    public event PropertyChangedEventHandler PropertyChanged;

    [Category("_")]
    [DisplayName("Full path")]
    [Description("Location of the item in the file system.")]
    public PathEx FullPath { get; private set; } = (PathEx)string.Empty;

    [Browsable(false)]
    public ISettingsService SS { get; set; }

    [Category(SettingsService.KindDebugger)]
    [DisplayName("Command line arguments")]
    [Description("Command line arguments passed to executable during F5 & CTRL+F5. Example: \"Arg 1\" arg2 arg3")]
    public string CommandLineArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsService.KindDebugger)]
    [DisplayName("Environment")]
    [Description("Environment passed to executable during F5 & CTRL+F5. Example: \"ENV VAR1=VAL 1\" ENVVAR2=VAL2")]
    public string DebuggerEnvironment
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsService.KindBuild)]
    [DisplayName("Additional arguments")]
    [Description("Additional build arguments passed Cargo.exe. Example: --features=blocking --config http.proxy=\\\"http://example.com\\\"")]
    public string AdditionalBuildArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    // TODO: TXP: Run example tests as well --all-targets.
    [Category(SettingsService.KindTest)]
    [DisplayName("Additional discovery arguments")]
    [Description($"Additional arguments passed Cargo.exe test --no-run in addition to --manifest-path and --profile. Check cargo help test for more information.")]
    public string AdditionalTestDiscoveryArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    // TODO: RELEASE: Write the command likes properly from VS logs.
    [Category(SettingsService.KindTest)]
    [DisplayName("Additional execution arguments")]
    [Description($"Additional arguments passed test executable test in addition to XXXX. Check cargo help test for more information.")]
    public string AdditionalTestExecutionArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsService.KindTest)]
    [DisplayName("Execution environment")]
    [Description($"Environment variables to set for test execution. Example: RUST_BACKTRACE=1")]
    public string TestExecutionEnvironment
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    public void ResetForNewNode(PathEx fullPath, ISettingsService ss)
    {
        FullPath = fullPath;
        SS = ss;
        foreach (var propName in SettingsService.PropertyInfo.Keys)
        {
            _propertyValueStore[propName] = SS.GetRaw(propName, FullPath);
        }
    }

    private string GetPropertyValue([CallerMemberName] string propertyName = "")
    {
        if (!_propertyValueStore.ContainsKey(propertyName))
        {
            _propertyValueStore[propertyName] = string.Empty;
        }

        return _propertyValueStore[propertyName];
    }

    private void SetPropertyValue(string value, [CallerMemberName] string propertyName = "")
    {
        if (value != GetPropertyValue(propertyName))
        {
            _propertyValueStore[propertyName] = value;
            NotifyPropertyChanged(propertyName);
        }
    }

    private void NotifyPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
