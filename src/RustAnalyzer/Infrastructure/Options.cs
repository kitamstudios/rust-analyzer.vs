using System.ComponentModel;
using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;

namespace KS.RustAnalyzer.Infrastructure;

public class OptionsProvider
{
    [ComVisible(true)]
    public class GeneralOptions : BaseOptionPage<Options>
    {
    }
}

public interface ISettingsServiceDefaults
{
    public string CommandLineArguments { get; set; }

    public string DebuggerEnvironment { get; set; }

    public string WorkingDirectory { get; set; }

    public string AdditionalBuildArguments { get; set; }

    public string AdditionalTestDiscoveryArguments { get; set; }

    public string AdditionalTestExecutionArguments { get; set; }

    public string TestExecutionEnvironment { get; set; }
}

public class Options : BaseOptionModel<Options>, ISettingsServiceDefaults
{
    [Browsable(true)]
    [Category(SettingsInfo.KindBuild)]
    [DisplayName("Default Clippy Arguments")]
    [Description("Command line arguments passed to cargo clippy. Default: --all-targets --all-features -- -D warnings")]
    public string DefaultCargoClippyArgs { get; set; } = "--all-targets --all-features -- -D warnings";

    [Browsable(true)]
    [Category(SettingsInfo.KindBuild)]
    [DisplayName("Default Cargo Arguments")]
    [Description("Command line arguments passed to cargo fmt. Default: --all")]
    public string DefaultCargoFmtArgs { get; set; } = "--all";

    [Browsable(false)]
    [Category(SettingsInfo.KindDebugger)]
    [DisplayName("Command line arguments")]
    [Description("Command line arguments passed to executable during F5 & CTRL+F5. Example: \"Arg 1\" arg2 arg3")]
    public string CommandLineArguments { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsInfo.KindDebugger)]
    [DisplayName("Environment")]
    [Description("Environment passed to executable during F5 & CTRL+F5. Example: \"ENV VAR1=VAL 1\" ENVVAR2=VAL2")]
    public string DebuggerEnvironment { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsInfo.KindDebugger)]
    [DisplayName("Working directory")]
    [Description("Working directory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsInfo.KindBuild)]
    [DisplayName("Additional arguments")]
    [Description("Additional build arguments passed Cargo.exe. Example: --features=blocking --config http.proxy=\\\"http://example.com\\\"")]
    public string AdditionalBuildArguments { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsInfo.KindTest)]
    [DisplayName("Discovery arguments")]
    [Description($"Additional arguments passed Cargo.exe test in addition to --no-run --manifest-path <manifest> --profile <profile>. Check 'cargo help test' for more information.")]
    public string AdditionalTestDiscoveryArguments { get; set; } = string.Empty;

    [Browsable(true)]
    [Category(SettingsInfo.KindTest)]
    [DisplayName("Execution arguments")]
    [Description($"Additional arguments passed test executable test in addition to --format json --report-time. Check 'cargo help test' for more information.")]
    public string AdditionalTestExecutionArguments { get; set; } = "--show-output --test-threads 1";

    [Browsable(true)]
    [Category(SettingsInfo.KindTest)]
    [DisplayName("Execution environment")]
    [Description($"Additioanal environment variables to set for test execution. Default: RUST_BACKTRACE=full. Example: RUST_BACKTRACE=1.")]
    public string TestExecutionEnvironment { get; set; } = "RUST_BACKTRACE=full";
}
