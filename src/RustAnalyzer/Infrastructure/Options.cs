using System.ComponentModel;
using System.Drawing.Design;
using System.IO;
using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

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

    public string LspInitializationOptions { get; set; }

    public string RustAnalyzerEnvArguments { get; set; }

    public bool EnableRustAnalyzerStderrLogging { get; set; }
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

    [Browsable(true)]
    [Category(SettingsInfo.KindConfig)]
    [DisplayName("Rust analyzer lsp initialization initializationOptions")]
    [Description("JSON object parsed to initializationOptions, see https://rust-analyzer.github.io/book/configuration\n" +
        "you can overwrite this global setting by placing a file named 'lsp_initializationOptions.json' in your top level project directory" +
        "the two files will then get merged using a deep merge strategy (see https://jsoncompare.com/json-merge-tool)\n" +
        "!!Make shure the json you enter is escaped correctly (no\\ in Windows paths or sth)\n" +
        "Restarting VS is required for changes to this settign (or the .json) to take effect")]
    [Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(UITypeEditor))]
    public string LspInitializationOptions { get; set; } = "{}";

    public JObject GetMergedLspInitializationOptions(string projectRootDir)
    {
        var configString = LspInitializationOptions;
        var lspInitOpsPath = !string.IsNullOrEmpty(projectRootDir) ? Path.Combine(projectRootDir, "lsp_initializationOptions.json") : null;
        var globalInitOps = new JObject();
        var localInitOps = new JObject();

        if (!string.IsNullOrWhiteSpace(configString))
        {
            try
            {
                globalInitOps = JObject.Parse(configString);
            }
            catch (System.Exception ex)
            {
                RustAnalyzerPackage.JTF.RunAsync(async () => 
                {
                    await VsCommon.ShowErrorMessageAsync(
                        "Rust Analyzer",
                        $"Error parsing LSP initialization options from settings: {ex.Message}\n\nPlease check your LSP initialization options in Tools > Options > Rust Analyzer > General.");
                }).FireAndForget();
                globalInitOps = new JObject();
            }
        }

        if (!string.IsNullOrEmpty(lspInitOpsPath) && File.Exists(lspInitOpsPath))
        {
            try
            {
                string overrideString = File.ReadAllText(lspInitOpsPath);
                if (!string.IsNullOrWhiteSpace(overrideString))
                {
                    localInitOps = JObject.Parse(overrideString);
                }
            }
            catch (IOException ex)
            {
                RustAnalyzerPackage.JTF.RunAsync(async () => 
                {
                    await VsCommon.ShowErrorMessageAsync(
                        "Rust Analyzer",
                        $"Error reading JSON file from '{lspInitOpsPath}': {ex.Message}");
                }).FireAndForget();
                localInitOps = new JObject();
            }
            catch (System.Exception ex)
            {
                RustAnalyzerPackage.JTF.RunAsync(async () => 
                {
                    await VsCommon.ShowErrorMessageAsync(
                        "Rust Analyzer",
                        $"Error parsing JSON from file '{lspInitOpsPath}': {ex.Message}");
                }).FireAndForget();
                localInitOps = new JObject();
            }
        }

        try
        {
            globalInitOps.Merge(localInitOps, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace,
                MergeNullValueHandling = MergeNullValueHandling.Merge
            });
        }
        catch (System.Exception ex)
        {
            RustAnalyzerPackage.JTF.RunAsync(async () => 
            {
                await VsCommon.ShowErrorMessageAsync(
                    "Rust Analyzer", 
                    $"Error merging LSP initialization options: {ex.Message}");
            }).FireAndForget();
            return new JObject();
        }

        return globalInitOps;
    }

    [Browsable(true)]
    [Category(SettingsInfo.KindConfig)]
    [DisplayName("Rust analyzer environment variables")]
    [Description("Environment variables passed to rust-analyzer.exe, example:\nRA_LOG=info Env2=Test Env3=Hello")]
    public string RustAnalyzerEnvArguments { get; set; } = string.Empty;

    public JObject GetRustAnalyzerEnvArguments()
    {
        var envArgs = new JObject();
        foreach (var arg in RustAnalyzerEnvArguments.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var kvp = arg.Split(new[] { '=' }, 2);

            // Set Env inputs without = to empty string
            var val = string.Empty;
            if (kvp.Length == 2)
            {
                val = kvp[1];
            }

            envArgs[kvp[0]] = val;
        }

        return envArgs;
    }

    [Browsable(true)]
    [Category(SettingsInfo.KindConfig)]
    [DisplayName("Enable Rust Analyzer Stderr Logging")]
    [Description("If enabled, output from rust-analyzer will be logged to the Output window.")]
    public bool EnableRustAnalyzerStderrLogging { get; set; } = false;

}
