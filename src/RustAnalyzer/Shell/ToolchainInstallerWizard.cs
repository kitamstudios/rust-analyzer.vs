using System;
using System.Linq;
using System.Windows.Forms;
using KS.RustAnalyzer.TestAdapter.Cargo;

namespace KS.RustAnalyzer.Shell;

public partial class ToolchainInstallerWizard : Form
{
    public ToolchainInstallerWizard()
    {
        InitializeComponent();
    }

    public string[] Targets { get; set; }

    public (string CmdLine, string TcName) GetCommandLineInfo()
    {
        return ($@"toolchain install {GetToolChainName()}{GetAdditionalTargetsArg()} --profile default", GetToolChainName());

        string GetToolChainName()
        {
            return $"{GetChannelPart()}{GetDatePart()}{GetTargetPart()}";
        }

        string GetChannelPart()
        {
            return comboBoxChannel.Text;
        }

        string GetDatePart()
        {
            if (dateTimePickerDate.Value == DateTime.Now)
            {
                return string.Empty;
            }

            var val = dateTimePickerDate.Value.ToString("yyyy-MM-dd");
            return $"-{val}";
        }

        string GetTargetPart()
        {
            return $"-{ToolChainServiceExtensions.AlwaysAvailableTarget}";
        }

        string GetAdditionalTargetsArg()
        {
            if (listBoxTargets.SelectedItems.Count == 0)
            {
                return string.Empty;
            }

            return string.Format(" --target {0}", string.Join(" ", listBoxTargets.SelectedItems.Cast<string>()));
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            DialogResult = DialogResult.Cancel;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private string GetCommandLineWitRustUp()
    {
        return $@"rustup {GetCommandLineInfo().CmdLine}";
    }

    private void ToolchainInstallerWizard_Load(object sender, EventArgs e)
    {
        comboBoxChannel.Items.AddRange(new string[] { "stable", "beta", "nightly" });
        comboBoxChannel.SelectedIndex = 0;

        dateTimePickerDate.Value = DateTime.Now;

        listBoxTargets.Items.AddRange(Targets);
    }

    private void ComboBoxChannel_SelectedIndexChanged(object sender, EventArgs e)
    {
        textBoxCommandLine.Text = GetCommandLineWitRustUp();
    }

    private void DateTimePickerDate_ValueChanged(object sender, EventArgs e)
    {
        textBoxCommandLine.Text = GetCommandLineWitRustUp();
    }

    private void ListBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        textBoxCommandLine.Text = GetCommandLineWitRustUp();
    }

    private void LinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        if (sender == labelChannel || sender == labelDate)
        {
            System.Diagnostics.Process.Start("https://rust-lang.github.io/rustup/concepts/toolchains.html");
        }
        else if (sender == labelTargets)
        {
            System.Diagnostics.Process.Start("https://rust-lang.github.io/rustup/cross-compilation.html");
        }
        else
        {
            throw new ArgumentOutOfRangeException("Unknown event");
        }
    }
}
