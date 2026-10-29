namespace CncController.WinForms;

partial class SimulatorForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblState = new Label();
        lblPosition = new Label();
        lblTime = new Label();
        lblSpeed = new Label();
        cmbSpeed = new ComboBox();
        chkAutoRestart = new CheckBox();
        lblNextRestart = new Label();
        btnRestart = new Button();
        btnCorrupt = new Button();
        btnUnplug = new Button();
        timerRefresh = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        //
        // lblState
        //
        lblState.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblState.Location = new Point(12, 9);
        lblState.Name = "lblState";
        lblState.Size = new Size(336, 22);
        //
        // lblPosition
        //
        lblPosition.Location = new Point(12, 34);
        lblPosition.Name = "lblPosition";
        lblPosition.Size = new Size(336, 20);
        //
        // lblTime
        //
        lblTime.Location = new Point(12, 56);
        lblTime.Name = "lblTime";
        lblTime.Size = new Size(336, 20);
        //
        // lblSpeed
        //
        lblSpeed.AutoSize = true;
        lblSpeed.Location = new Point(12, 88);
        lblSpeed.Name = "lblSpeed";
        lblSpeed.Text = "Brzina vremena:";
        //
        // cmbSpeed
        //
        cmbSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSpeed.Location = new Point(120, 85);
        cmbSpeed.Name = "cmbSpeed";
        cmbSpeed.Size = new Size(80, 23);
        cmbSpeed.TabIndex = 0;
        cmbSpeed.SelectedIndexChanged += cmbSpeed_SelectedIndexChanged;
        //
        // chkAutoRestart
        //
        chkAutoRestart.AutoSize = true;
        chkAutoRestart.Location = new Point(12, 118);
        chkAutoRestart.Name = "chkAutoRestart";
        chkAutoRestart.TabIndex = 1;
        chkAutoRestart.Text = "Restart svakih 15 min (smetnje u napajanju)";
        chkAutoRestart.CheckedChanged += chkAutoRestart_CheckedChanged;
        //
        // lblNextRestart
        //
        lblNextRestart.ForeColor = Color.FromArgb(110, 110, 110);
        lblNextRestart.Location = new Point(240, 119);
        lblNextRestart.Name = "lblNextRestart";
        lblNextRestart.Size = new Size(108, 20);
        //
        // btnRestart
        //
        btnRestart.Location = new Point(12, 150);
        btnRestart.Name = "btnRestart";
        btnRestart.Size = new Size(336, 32);
        btnRestart.TabIndex = 2;
        btnRestart.Text = "Restartuj Arduino (smetnja u napajanju)";
        btnRestart.Click += btnRestart_Click;
        //
        // btnCorrupt
        //
        btnCorrupt.Location = new Point(12, 188);
        btnCorrupt.Name = "btnCorrupt";
        btnCorrupt.Size = new Size(165, 30);
        btnCorrupt.TabIndex = 3;
        btnCorrupt.Text = "Pokvari sljedeći status";
        btnCorrupt.Click += btnCorrupt_Click;
        //
        // btnUnplug
        //
        btnUnplug.Location = new Point(183, 188);
        btnUnplug.Name = "btnUnplug";
        btnUnplug.Size = new Size(165, 30);
        btnUnplug.TabIndex = 4;
        btnUnplug.Text = "Izvuci USB kabl";
        btnUnplug.Click += btnUnplug_Click;
        //
        // timerRefresh
        //
        timerRefresh.Enabled = true;
        timerRefresh.Interval = 200;
        timerRefresh.Tick += timerRefresh_Tick;
        //
        // SimulatorForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(360, 230);
        Controls.Add(lblState);
        Controls.Add(lblPosition);
        Controls.Add(lblTime);
        Controls.Add(lblSpeed);
        Controls.Add(cmbSpeed);
        Controls.Add(chkAutoRestart);
        Controls.Add(lblNextRestart);
        Controls.Add(btnRestart);
        Controls.Add(btnCorrupt);
        Controls.Add(btnUnplug);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Name = "SimulatorForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Simulirani Grbl 0.8c";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblState;
    private Label lblPosition;
    private Label lblTime;
    private Label lblSpeed;
    private ComboBox cmbSpeed;
    private CheckBox chkAutoRestart;
    private Label lblNextRestart;
    private Button btnRestart;
    private Button btnCorrupt;
    private Button btnUnplug;
    private System.Windows.Forms.Timer timerRefresh;
}
