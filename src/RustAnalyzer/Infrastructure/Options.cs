using System.ComponentModel;
using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;

namespace KS.RustAnalyzer.Infrastructure;

public partial class OptionsProvider
{
    [ComVisible(true)]
    public class GeneralOptions : BaseOptionPage<Options>
    {
    }
}

public class Options : BaseOptionModel<Options>, IRatingConfig
{
    [DisplayName("Default Clippy Arguments")]
    [Description("Command line arguments passed to cargo clippy. Default is: --all-targets --all-features -- -D warnings")]
    public string DefailtCargoClippyArgs { get; set; } = "--all-targets --all-features -- -D warnings";

    [DisplayName("Default Cargo Arguments")]
    [Description("Command line arguments passed to cargo fmt. Default is: --all")]
    public string DefailtCargoFmtArgs { get; set; } = "--all";

    [Browsable(false)]
    public int RatingRequests { get; set; }
}
