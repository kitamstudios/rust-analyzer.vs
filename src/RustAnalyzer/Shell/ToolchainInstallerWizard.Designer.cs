namespace KS.RustAnalyzer.Shell;

partial class ToolchainInstallerWizard
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ToolchainInstallerWizard));
            this.tableLayoutPanelRoot = new System.Windows.Forms.TableLayoutPanel();
            this.labelSplitter3 = new System.Windows.Forms.Label();
            this.comboBoxChannel = new System.Windows.Forms.ComboBox();
            this.dateTimePickerDate = new System.Windows.Forms.DateTimePicker();
            this.labelDate = new System.Windows.Forms.LinkLabel();
            this.listBoxTargets = new System.Windows.Forms.ListBox();
            this.labelSplitter1 = new System.Windows.Forms.Label();
            this.labelChannel = new System.Windows.Forms.LinkLabel();
            this.labelTargets = new System.Windows.Forms.LinkLabel();
            this.buttonInstall = new System.Windows.Forms.Button();
            this.labelSplitter2 = new System.Windows.Forms.Label();
            this.textBoxCommandLine = new System.Windows.Forms.TextBox();
            this.labelCommandLine = new System.Windows.Forms.Label();
            this.toolTipRoot = new System.Windows.Forms.ToolTip(this.components);
            this.tableLayoutPanelRoot.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanelRoot
            // 
            this.tableLayoutPanelRoot.ColumnCount = 3;
            this.tableLayoutPanelRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanelRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanelRoot.Controls.Add(this.labelSplitter3, 0, 7);
            this.tableLayoutPanelRoot.Controls.Add(this.comboBoxChannel, 1, 0);
            this.tableLayoutPanelRoot.Controls.Add(this.dateTimePickerDate, 1, 1);
            this.tableLayoutPanelRoot.Controls.Add(this.labelDate, 0, 1);
            this.tableLayoutPanelRoot.Controls.Add(this.listBoxTargets, 1, 4);
            this.tableLayoutPanelRoot.Controls.Add(this.labelSplitter1, 1, 2);
            this.tableLayoutPanelRoot.Controls.Add(this.labelChannel, 0, 0);
            this.tableLayoutPanelRoot.Controls.Add(this.labelTargets, 0, 4);
            this.tableLayoutPanelRoot.Controls.Add(this.buttonInstall, 2, 8);
            this.tableLayoutPanelRoot.Controls.Add(this.labelSplitter2, 0, 5);
            this.tableLayoutPanelRoot.Controls.Add(this.textBoxCommandLine, 1, 6);
            this.tableLayoutPanelRoot.Controls.Add(this.labelCommandLine, 0, 6);
            this.tableLayoutPanelRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelRoot.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelRoot.Name = "tableLayoutPanelRoot";
            this.tableLayoutPanelRoot.RowCount = 9;
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 66.66666F));
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanelRoot.Size = new System.Drawing.Size(778, 444);
            this.tableLayoutPanelRoot.TabIndex = 1;
            // 
            // labelSplitter3
            // 
            this.labelSplitter3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelSplitter3.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.tableLayoutPanelRoot.SetColumnSpan(this.labelSplitter3, 3);
            this.labelSplitter3.Location = new System.Drawing.Point(15, 355);
            this.labelSplitter3.Margin = new System.Windows.Forms.Padding(15, 8, 15, 8);
            this.labelSplitter3.Name = "labelSplitter3";
            this.labelSplitter3.Size = new System.Drawing.Size(748, 5);
            this.labelSplitter3.TabIndex = 12;
            // 
            // comboBoxChannel
            // 
            this.comboBoxChannel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBoxChannel.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.comboBoxChannel.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.tableLayoutPanelRoot.SetColumnSpan(this.comboBoxChannel, 2);
            this.comboBoxChannel.FormattingEnabled = true;
            this.comboBoxChannel.Location = new System.Drawing.Point(141, 8);
            this.comboBoxChannel.Margin = new System.Windows.Forms.Padding(8, 8, 15, 8);
            this.comboBoxChannel.Name = "comboBoxChannel";
            this.comboBoxChannel.Size = new System.Drawing.Size(622, 28);
            this.comboBoxChannel.TabIndex = 3;
            this.toolTipRoot.SetToolTip(this.comboBoxChannel, "stable | beta | nightly | <major.minor> | <major.minor.patch>");
            this.comboBoxChannel.SelectedIndexChanged += new System.EventHandler(this.ComboBoxChannel_SelectedIndexChanged);
            // 
            // dateTimePickerDate
            // 
            this.dateTimePickerDate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelRoot.SetColumnSpan(this.dateTimePickerDate, 2);
            this.dateTimePickerDate.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this.dateTimePickerDate.Location = new System.Drawing.Point(141, 52);
            this.dateTimePickerDate.Margin = new System.Windows.Forms.Padding(8, 8, 15, 8);
            this.dateTimePickerDate.Name = "dateTimePickerDate";
            this.dateTimePickerDate.Size = new System.Drawing.Size(622, 26);
            this.dateTimePickerDate.TabIndex = 5;
            this.toolTipRoot.SetToolTip(this.dateTimePickerDate, "YYYY-MM-DD. Keep today\'s date to not add the date component to toolchain specific" +
        "ations.");
            this.dateTimePickerDate.ValueChanged += new System.EventHandler(this.DateTimePickerDate_ValueChanged);
            // 
            // labelDate
            // 
            this.labelDate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelDate.AutoSize = true;
            this.labelDate.Location = new System.Drawing.Point(15, 55);
            this.labelDate.Margin = new System.Windows.Forms.Padding(15, 8, 8, 8);
            this.labelDate.Name = "labelDate";
            this.labelDate.Size = new System.Drawing.Size(110, 20);
            this.labelDate.TabIndex = 4;
            this.labelDate.TabStop = true;
            this.labelDate.Text = "Date";
            this.labelDate.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LinkLabel_LinkClicked);
            // 
            // listBoxTargets
            // 
            this.listBoxTargets.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelRoot.SetColumnSpan(this.listBoxTargets, 2);
            this.listBoxTargets.FormattingEnabled = true;
            this.listBoxTargets.IntegralHeight = false;
            this.listBoxTargets.ItemHeight = 20;
            this.listBoxTargets.Location = new System.Drawing.Point(141, 115);
            this.listBoxTargets.Margin = new System.Windows.Forms.Padding(8, 8, 15, 8);
            this.listBoxTargets.Name = "listBoxTargets";
            this.listBoxTargets.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.listBoxTargets.Size = new System.Drawing.Size(622, 130);
            this.listBoxTargets.TabIndex = 7;
            this.toolTipRoot.SetToolTip(this.listBoxTargets, "To cross-compile to other platforms you must install one or more other target pla" +
        "tforms. x86_64-pc-windows-msvc will be installed by default.");
            this.listBoxTargets.SelectedIndexChanged += new System.EventHandler(this.ListBox_SelectedIndexChanged);
            // 
            // labelSplitter1
            // 
            this.labelSplitter1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelSplitter1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.tableLayoutPanelRoot.SetColumnSpan(this.labelSplitter1, 3);
            this.labelSplitter1.Location = new System.Drawing.Point(15, 94);
            this.labelSplitter1.Margin = new System.Windows.Forms.Padding(15, 8, 15, 8);
            this.labelSplitter1.Name = "labelSplitter1";
            this.labelSplitter1.Size = new System.Drawing.Size(748, 5);
            this.labelSplitter1.TabIndex = 7;
            // 
            // labelChannel
            // 
            this.labelChannel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelChannel.AutoSize = true;
            this.labelChannel.Location = new System.Drawing.Point(15, 12);
            this.labelChannel.Margin = new System.Windows.Forms.Padding(15, 8, 8, 8);
            this.labelChannel.Name = "labelChannel";
            this.labelChannel.Size = new System.Drawing.Size(110, 20);
            this.labelChannel.TabIndex = 2;
            this.labelChannel.TabStop = true;
            this.labelChannel.Text = "Channel";
            this.labelChannel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LinkLabel_LinkClicked);
            // 
            // labelTargets
            // 
            this.labelTargets.AutoSize = true;
            this.labelTargets.Location = new System.Drawing.Point(15, 115);
            this.labelTargets.Margin = new System.Windows.Forms.Padding(15, 8, 8, 8);
            this.labelTargets.Name = "labelTargets";
            this.labelTargets.Size = new System.Drawing.Size(73, 20);
            this.labelTargets.TabIndex = 6;
            this.labelTargets.TabStop = true;
            this.labelTargets.Text = "Target(s)";
            this.labelTargets.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LinkLabel_LinkClicked);
            // 
            // buttonInstall
            // 
            this.buttonInstall.AutoSize = true;
            this.buttonInstall.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonInstall.Location = new System.Drawing.Point(653, 383);
            this.buttonInstall.Margin = new System.Windows.Forms.Padding(15);
            this.buttonInstall.Name = "buttonInstall";
            this.buttonInstall.Size = new System.Drawing.Size(110, 46);
            this.buttonInstall.TabIndex = 1;
            this.buttonInstall.Text = "Install...";
            this.toolTipRoot.SetToolTip(this.buttonInstall, "Start installation...");
            this.buttonInstall.UseVisualStyleBackColor = true;
            // 
            // labelSplitter2
            // 
            this.labelSplitter2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelSplitter2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.tableLayoutPanelRoot.SetColumnSpan(this.labelSplitter2, 3);
            this.labelSplitter2.Location = new System.Drawing.Point(15, 261);
            this.labelSplitter2.Margin = new System.Windows.Forms.Padding(15, 8, 15, 8);
            this.labelSplitter2.Name = "labelSplitter2";
            this.labelSplitter2.Size = new System.Drawing.Size(748, 5);
            this.labelSplitter2.TabIndex = 9;
            // 
            // textBoxCommandLine
            // 
            this.textBoxCommandLine.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelRoot.SetColumnSpan(this.textBoxCommandLine, 2);
            this.textBoxCommandLine.Location = new System.Drawing.Point(141, 282);
            this.textBoxCommandLine.Margin = new System.Windows.Forms.Padding(8, 8, 15, 8);
            this.textBoxCommandLine.Multiline = true;
            this.textBoxCommandLine.Name = "textBoxCommandLine";
            this.textBoxCommandLine.ReadOnly = true;
            this.textBoxCommandLine.Size = new System.Drawing.Size(622, 57);
            this.textBoxCommandLine.TabIndex = 8;
            this.toolTipRoot.SetToolTip(this.textBoxCommandLine, "This will be the command executed. You may also copy, paste & execute it yourself" +
        " from ommand line.");
            // 
            // labelCommandLine
            // 
            this.labelCommandLine.AutoSize = true;
            this.labelCommandLine.Location = new System.Drawing.Point(15, 282);
            this.labelCommandLine.Margin = new System.Windows.Forms.Padding(15, 8, 8, 8);
            this.labelCommandLine.Name = "labelCommandLine";
            this.labelCommandLine.Size = new System.Drawing.Size(110, 20);
            this.labelCommandLine.TabIndex = 11;
            this.labelCommandLine.Text = "Command line";
            // 
            // ToolchainInstallerWizard
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(778, 444);
            this.Controls.Add(this.tableLayoutPanelRoot);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ToolchainInstallerWizard";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Toolchain Installer";
            this.Load += new System.EventHandler(this.ToolchainInstallerWizard_Load);
            this.tableLayoutPanelRoot.ResumeLayout(false);
            this.tableLayoutPanelRoot.PerformLayout();
            this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.TableLayoutPanel tableLayoutPanelRoot;
    private System.Windows.Forms.LinkLabel labelChannel;
    private System.Windows.Forms.LinkLabel labelDate;
    private System.Windows.Forms.Button buttonInstall;
    private System.Windows.Forms.ComboBox comboBoxChannel;
    private System.Windows.Forms.DateTimePicker dateTimePickerDate;
    private System.Windows.Forms.ListBox listBoxTargets;
    private System.Windows.Forms.Label labelSplitter1;
    private System.Windows.Forms.LinkLabel labelTargets;
    private System.Windows.Forms.Label labelSplitter2;
    private System.Windows.Forms.TextBox textBoxCommandLine;
    private System.Windows.Forms.Label labelCommandLine;
    private System.Windows.Forms.Label labelSplitter3;
    private System.Windows.Forms.ToolTip toolTipRoot;
}