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

    public string AdditionalBuildArguments { get; set; }

    public string AdditionalTestDiscoveryArguments { get; set; }

    public string AdditionalTestExecutionArguments { get; set; }

    public string TestExecutionEnvironment { get; set; }
}

public class Options : BaseOptionModel<Options>, ISettingsServiceDefaults
{
    [Browsable(true)]
    [Category(SettingsService.KindBuild)]
    [DisplayName("Default Clippy Arguments")]
    [Description("Command line arguments passed to cargo clippy. Default is: --all-targets --all-features -- -D warnings")]
    public string DefailtCargoClippyArgs { get; set; } = "--all-targets --all-features -- -D warnings";

    [Browsable(true)]
    [Category(SettingsService.KindBuild)]
    [DisplayName("Default Cargo Arguments")]
    [Description("Command line arguments passed to cargo fmt. Default is: --all")]
    public string DefailtCargoFmtArgs { get; set; } = "--all";

    [Browsable(false)]
    [Category(SettingsService.KindDebugger)]
    [DisplayName("Command line arguments")]
    [Description("Command line arguments passed to executable during F5 & CTRL+F5. Example: \"Arg 1\" arg2 arg3")]
    public string CommandLineArguments { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsService.KindDebugger)]
    [DisplayName("Environment")]
    [Description("Environment passed to executable during F5 & CTRL+F5. Example: \"ENV VAR1=VAL 1\" ENVVAR2=VAL2")]
    public string DebuggerEnvironment { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsService.KindBuild)]
    [DisplayName("Additional arguments")]
    [Description("Additional build arguments passed Cargo.exe. Example: --features=blocking --config http.proxy=\\\"http://example.com\\\"")]
    public string AdditionalBuildArguments { get; set; } = string.Empty;

    [Browsable(false)]
    [Category(SettingsService.KindTest)]
    [DisplayName("Additional test discovery arguments")]
    [Description($"Additional arguments passed Cargo.exe test --no-run in addition to --manifest-path and --profile. Check cargo help test for more information.")]
    public string AdditionalTestDiscoveryArguments { get; set; } = string.Empty;

    [Browsable(true)]
    [Category(SettingsService.KindTest)]
    [DisplayName("Additional test execution arguments")]
    [Description($"Additional arguments passed test executable test in addition to XXXX. Check cargo help test for more information.")]
    public string AdditionalTestExecutionArguments { get; set; } = "--exact --show-output --test-threads 1";

    // TODO: RELEASE: Fix up the descriptions and default values
    [Browsable(true)]
    [Category(SettingsService.KindTest)]
    [DisplayName("Test execution environment")]
    [Description($"Environment variables to set for test execution. Example: RUST_BACKTRACE=1")]
    public string TestExecutionEnvironment { get; set; } = "RUST_BACKTRACE=full";
}
