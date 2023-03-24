using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.NodeEnhancements;

// TODO: TXP: Support doc, example, benchmark and integration tests. Run example tests as well --all-targets.
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

    [Category(SettingsInfo.KindDebugger)]
    [DisplayName("Command line arguments")]
    [Description("Command line arguments passed to executable during F5 & CTRL+F5. Example: \"Arg 1\" arg2 arg3")]
    public string CommandLineArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsInfo.KindDebugger)]
    [DisplayName("Environment")]
    [Description("Environment passed to executable during F5 & CTRL+F5. Example: \"ENV VAR1=VAL 1\" ENVVAR2=VAL2")]
    public string DebuggerEnvironment
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsInfo.KindBuild)]
    [DisplayName("Additional arguments")]
    [Description(@"Additional build arguments passed Cargo.exe build. Example: --features=blocking --config ""build.rustflags = '--cfg foo=\""bar\""'""")]
    public string AdditionalBuildArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsInfo.KindTest)]
    [DisplayName("Discovery arguments")]
    [Description("Additional arguments passed Cargo.exe test in addition to --no-run --manifest-path <manifest> --profile <profile>. Overrides Tools > Options > rust-analyzer.vs. Check 'cargo help test' for more information.")]
    public string AdditionalTestDiscoveryArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsInfo.KindTest)]
    [DisplayName("Execution arguments")]
    [Description("Additional arguments passed test executable test in addition to --format json --report-time. Overrides Tools > Options > rust-analyzer.vs. Check 'cargo help test' for more information.")]
    public string AdditionalTestExecutionArguments
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    [Category(SettingsInfo.KindTest)]
    [DisplayName("Execution environment")]
    [Description("Environment variables to set for test execution. Overrides Tools > Options > rust-analyzer.vs. Example: RUST_BACKTRACE=1")]
    public string TestExecutionEnvironment
    {
        get => GetPropertyValue();
        set => SetPropertyValue(value);
    }

    public void ResetForNewNode(PathEx fullPath, ISettingsService ss)
    {
        FullPath = fullPath;
        SS = ss;
        foreach (var propName in SettingsInfo.Store.Keys)
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
