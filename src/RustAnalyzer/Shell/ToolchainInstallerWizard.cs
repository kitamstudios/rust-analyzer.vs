using System;
using System.Linq;
using System.Windows.Forms;

namespace KS.RustAnalyzer.Shell;

public partial class ToolchainInstallerWizard : Form
{
    public ToolchainInstallerWizard()
    {
        InitializeComponent();
    }

    public string[] Targets { get; set; }

    public string GetCommandLine()
    {
        return $@"toolchain install {comboBoxChannel.Text}-{GetDate()}-x86_64-pc-windows-msvc {GetTargetsArg()}--profile default";

        string GetDate()
        {
            var val = dateTimePickerDate.Value.ToString("yyyy-MM-dd");

            if (val == DateTime.Now.ToString("yyyy-MM-dd"))
            {
                return string.Empty;
            }
            else
            {
                return val;
            }
        }

        string GetTargetsArg()
        {
            if (listBoxTargets.SelectedItems.Count != 0)
            {
                return string.Format("--target {0} ", string.Join(" ", listBoxTargets.SelectedItems.Cast<string>()));
            }
            else
            {
                return string.Empty;
            }
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
        return $@"rustup {GetCommandLine()}";
    }

    private void ToolchainInstallerWizard_Load(object sender, System.EventArgs e)
    {
        comboBoxChannel.Items.AddRange(new string[] { "stable", "beta", "nightly" });
        comboBoxChannel.SelectedIndex = 0;

        dateTimePickerDate.Value = DateTime.Now;

        listBoxTargets.Items.AddRange(Targets);
    }

    private void ComboBoxChannel_SelectedIndexChanged(object sender, EventArgs e)
    {
        textBoxCommandLine.Text = GetCommandLine();
    }

    private void DateTimePickerDate_ValueChanged(object sender, EventArgs e)
    {
        textBoxCommandLine.Text = GetCommandLine();
    }

    private void ListBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        textBoxCommandLine.Text = GetCommandLine();
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
