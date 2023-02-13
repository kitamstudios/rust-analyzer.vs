using System.ComponentModel;
using System.Runtime.CompilerServices;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.NodeEnhancements;

public sealed partial class NodeBrowseObjectProvider
{
    public class BrowseObject : INotifyPropertyChanged
    {
        private string _commandLineArguments;
        private string _debuggerEnvironment;
        private string _additionalBuildArguments;

        public event PropertyChangedEventHandler PropertyChanged;

        [Browsable(false)]
        public string RelativePath { get; set; }

        [Browsable(false)]
        public ISettingsService SS { get; set; }

        [Category(SettingsService.KindDebugger)]
        [DisplayName("Command line arguments")]
        [Description("Command line arguments passed to executable during F5 & CTRL+F5. Example: Arg1 arg2 arg3")]
        public string CommandLineArguments
        {
            get
            {
                return _commandLineArguments;
            }

            set
            {
                if (value != _commandLineArguments)
                {
                    _commandLineArguments = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [Category(SettingsService.KindDebugger)]
        [DisplayName("Environment")]
        [Description("Environment passed to executable during F5 & CTRL+F5. Example: ENVVAR1=VAL1 ENVVAR2=VAL2")]
        public string DebuggerEnvironment
        {
            get
            {
                return _debuggerEnvironment;
            }

            set
            {
                if (value != _debuggerEnvironment)
                {
                    _debuggerEnvironment = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [Category(SettingsService.KindBuild)]
        [DisplayName("Additional arguments")]
        [Description($"Additional build arguments passed Cargo.exe. Example:  --features=blocking")]
        public string AdditionalBuildArguments
        {
            get
            {
                return _additionalBuildArguments;
            }

            set
            {
                if (value != _additionalBuildArguments)
                {
                    _additionalBuildArguments = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public void ResetForNewNode(string relativePath, ISettingsService ss, string cmdLineArgs, string dbgEnv, string additionalBuildArgs)
        {
            RelativePath = relativePath;
            SS = ss;
            _commandLineArguments = cmdLineArgs;
            _debuggerEnvironment = dbgEnv;
            _additionalBuildArguments = additionalBuildArgs;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
