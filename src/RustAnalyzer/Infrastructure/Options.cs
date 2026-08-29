using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Community.VisualStudio.Toolkit;
using EnsureThat;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace KS.RustAnalyzer.Infrastructure;

public class OptionsProvider
{
    [ComVisible(true)]
    public class GeneralOptions : DialogPage
    {
        private static readonly UnavailableOptions Unavailable = new();

        private readonly JoinableTaskFactory _joinableTaskFactory = RustAnalyzerPackage.JTF;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly PrerequisiteProcessState _prerequisiteState;
        private Options _options;
        private PropertyGrid _propertyGrid;
        private JoinableTask _readinessObservation;
        private int _disposed;

        public GeneralOptions()
            : this(PrerequisiteProcessState.Current)
        {
        }

        protected GeneralOptions(PrerequisiteProcessState prerequisiteState)
        {
            _prerequisiteState = EnsureArg.IsNotNull(
                prerequisiteState,
                nameof(prerequisiteState),
                options => options.WithException(
                    new ArgumentNullException(nameof(prerequisiteState))));
        }

        public override object AutomationObject =>
            PrerequisitesReady ? GetOptions() : Unavailable;

        protected override IWin32Window Window
        {
            get
            {
                if (_propertyGrid == null)
                {
                    _propertyGrid = (PropertyGrid)base.Window;
                    if (ReferenceEquals(_propertyGrid.SelectedObject, Unavailable))
                    {
                        _readinessObservation = _joinableTaskFactory.RunAsync(
                            () => ObserveReadinessAsync(
                                _lifetimeCancellation.Token));
                    }
                }

                return _propertyGrid;
            }
        }

        public override void LoadSettingsFromStorage()
        {
            if (PrerequisitesReady)
            {
                LoadOptions(GetOptions());
            }
        }

        public override void LoadSettingsFromXml(IVsSettingsReader reader)
        {
            if (PrerequisitesReady)
            {
                LoadOptionsFromXml(reader);
            }
        }

        public override void SaveSettingsToStorage()
        {
            if (PrerequisitesReady)
            {
                SaveOptions(GetOptions());
            }
        }

        public override void SaveSettingsToXml(IVsSettingsWriter writer)
        {
            if (PrerequisitesReady)
            {
                SaveOptionsToXml(writer);
            }
        }

        public override void ResetSettings()
        {
            if (PrerequisitesReady)
            {
                ResetOptions();
            }
        }

        protected virtual Options CreateOptions()
        {
            return ThreadHelper.JoinableTaskFactory.Run(Options.CreateAsync);
        }

        protected virtual void LoadOptions(Options options)
        {
            options.Load();
        }

        protected virtual void LoadOptionsFromXml(IVsSettingsReader reader)
        {
            base.LoadSettingsFromXml(reader);
        }

        protected virtual void SaveOptions(Options options)
        {
            options.Save();
        }

        protected virtual void SaveOptionsToXml(IVsSettingsWriter writer)
        {
            base.SaveSettingsToXml(writer);
        }

        protected virtual void ResetOptions()
        {
            base.ResetSettings();
        }

        protected override void Dispose(bool disposing)
        {
            var disposeLifetime = disposing &&
                Interlocked.CompareExchange(ref _disposed, 1, 0) == 0;
            if (disposeLifetime)
            {
                _lifetimeCancellation.Cancel();
                _readinessObservation?.Join();
            }

            base.Dispose(disposing);

            if (disposeLifetime)
            {
                _propertyGrid = null;
                _lifetimeCancellation.Dispose();
            }
        }

        private Options GetOptions()
        {
            return _options ??= CreateOptions();
        }

        private bool PrerequisitesReady =>
            _prerequisiteState?.IsAvailable == true;

        private async Task ObserveReadinessAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = _prerequisiteState.Status;
                    if (status == PrerequisiteStatus.Ready)
                    {
                        await _joinableTaskFactory.SwitchToMainThreadAsync(
                            cancellationToken);
                        if (Volatile.Read(ref _disposed) == 0 &&
                            _prerequisiteState.IsAvailable &&
                            _propertyGrid?.IsDisposed == false)
                        {
                            _propertyGrid.SelectedObject = GetOptions();
                        }

                        return;
                    }

                    if (status == PrerequisiteStatus.Failed ||
                        status == PrerequisiteStatus.Suspended)
                    {
                        return;
                    }

                    await _prerequisiteState.WaitForStatusChangeAsync(
                        status,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private sealed class UnavailableOptions
        {
            [Browsable(true)]
            [ReadOnly(true)]
            [Category(SettingsInfo.KindBuild)]
            [DisplayName("Default Clippy Arguments")]
            [Description("Command line arguments passed to cargo clippy. Default: --all-targets --all-features -- -D warnings")]
            public string DefaultCargoClippyArgs => string.Empty;

            [Browsable(true)]
            [ReadOnly(true)]
            [Category(SettingsInfo.KindBuild)]
            [DisplayName("Default Cargo Arguments")]
            [Description("Command line arguments passed to cargo fmt. Default: --all")]
            public string DefaultCargoFmtArgs => string.Empty;

            [Browsable(true)]
            [ReadOnly(true)]
            [Category(SettingsInfo.KindTest)]
            [DisplayName("Execution arguments")]
            [Description($"Additional arguments passed test executable test in addition to --format json --report-time. Check 'cargo help test' for more information.")]
            public string AdditionalTestExecutionArguments => string.Empty;

            [Browsable(true)]
            [ReadOnly(true)]
            [Category(SettingsInfo.KindTest)]
            [DisplayName("Execution environment")]
            [Description($"Additioanal environment variables to set for test execution. Default: RUST_BACKTRACE=full. Example: RUST_BACKTRACE=1.")]
            public string TestExecutionEnvironment => string.Empty;
        }
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
