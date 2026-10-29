namespace CncController.WinForms;

partial class LanDialog
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            picQr.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        lblInfo = new Label();
        chkEnabled = new CheckBox();
        lblPort = new Label();
        numPort = new NumericUpDown();
        lblCode = new Label();
        txtCode = new TextBox();
        btnNewCode = new Button();
        lblAddress = new Label();
        txtAddress = new TextBox();
        picQr = new PictureBox();
        lblFirewall = new Label();
        btnFirewall = new Button();
        lblStatus = new Label();
        btnOk = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)numPort).BeginInit();
        ((System.ComponentModel.ISupportInitialize)picQr).BeginInit();
        SuspendLayout();
        //
        // lblInfo
        //
        lblInfo.Location = new Point(12, 9);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(436, 48);
        lblInfo.Text = "Mobitel ili drugi računar na istoj mreži (WiFi) otvori stranicu programa direktno, bez sajta i interneta. Prvi put upiše kod ispod ili skenira QR kod.";
        //
        // chkEnabled
        //
        chkEnabled.AutoSize = true;
        chkEnabled.Location = new Point(12, 62);
        chkEnabled.Name = "chkEnabled";
        chkEnabled.TabIndex = 0;
        chkEnabled.Text = "Uključi pristup u lokalnoj mreži";
        chkEnabled.CheckedChanged += Inputs_Changed;
        //
        // lblPort
        //
        lblPort.AutoSize = true;
        lblPort.Location = new Point(12, 96);
        lblPort.Name = "lblPort";
        lblPort.Text = "Port:";
        //
        // numPort
        //
        numPort.Location = new Point(130, 92);
        numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        numPort.Minimum = new decimal(new int[] { 1024, 0, 0, 0 });
        numPort.Name = "numPort";
        numPort.Size = new Size(90, 23);
        numPort.TabIndex = 1;
        numPort.Value = new decimal(new int[] { 8090, 0, 0, 0 });
        numPort.ValueChanged += Inputs_Changed;
        //
        // lblCode
        //
        lblCode.AutoSize = true;
        lblCode.Location = new Point(12, 128);
        lblCode.Name = "lblCode";
        lblCode.Text = "Kod:";
        //
        // txtCode
        //
        txtCode.Font = new Font("Consolas", 11F, FontStyle.Bold);
        txtCode.Location = new Point(130, 123);
        txtCode.Name = "txtCode";
        txtCode.ReadOnly = true;
        txtCode.Size = new Size(90, 25);
        txtCode.TabStop = false;
        //
        // btnNewCode
        //
        btnNewCode.Location = new Point(228, 121);
        btnNewCode.Name = "btnNewCode";
        btnNewCode.Size = new Size(110, 29);
        btnNewCode.TabIndex = 2;
        btnNewCode.Text = "Novi kod";
        btnNewCode.Click += btnNewCode_Click;
        //
        // lblAddress
        //
        lblAddress.AutoSize = true;
        lblAddress.Location = new Point(12, 162);
        lblAddress.Name = "lblAddress";
        lblAddress.Text = "Adresa:";
        //
        // txtAddress
        //
        txtAddress.Location = new Point(130, 158);
        txtAddress.Name = "txtAddress";
        txtAddress.ReadOnly = true;
        txtAddress.Size = new Size(318, 23);
        txtAddress.TabStop = false;
        //
        // picQr
        //
        picQr.Location = new Point(130, 190);
        picQr.Name = "picQr";
        picQr.Size = new Size(220, 220);
        picQr.SizeMode = PictureBoxSizeMode.Zoom;
        //
        // lblFirewall
        //
        lblFirewall.Location = new Point(12, 418);
        lblFirewall.Name = "lblFirewall";
        lblFirewall.Size = new Size(300, 48);
        //
        // btnFirewall
        //
        btnFirewall.Location = new Point(318, 416);
        btnFirewall.Name = "btnFirewall";
        btnFirewall.Size = new Size(130, 29);
        btnFirewall.TabIndex = 3;
        btnFirewall.Text = "Dozvoli u firewallu";
        btnFirewall.Visible = false;
        btnFirewall.Click += btnFirewall_Click;
        //
        // lblStatus
        //
        lblStatus.ForeColor = Color.FromArgb(110, 110, 110);
        lblStatus.Location = new Point(12, 470);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(436, 50);
        //
        // btnOk
        //
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Location = new Point(264, 528);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(89, 30);
        btnOk.TabIndex = 4;
        btnOk.Text = "U redu";
        //
        // btnCancel
        //
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(359, 528);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(89, 30);
        btnCancel.TabIndex = 5;
        btnCancel.Text = "Odustani";
        //
        // LanDialog
        //
        AcceptButton = btnOk;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(460, 570);
        Controls.Add(lblInfo);
        Controls.Add(chkEnabled);
        Controls.Add(lblPort);
        Controls.Add(numPort);
        Controls.Add(lblCode);
        Controls.Add(txtCode);
        Controls.Add(btnNewCode);
        Controls.Add(lblAddress);
        Controls.Add(txtAddress);
        Controls.Add(picQr);
        Controls.Add(lblFirewall);
        Controls.Add(btnFirewall);
        Controls.Add(lblStatus);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "LanDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Lokalna mreža";
        ((System.ComponentModel.ISupportInitialize)numPort).EndInit();
        ((System.ComponentModel.ISupportInitialize)picQr).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblInfo;
    private CheckBox chkEnabled;
    private Label lblPort;
    private NumericUpDown numPort;
    private Label lblCode;
    private TextBox txtCode;
    private Button btnNewCode;
    private Label lblAddress;
    private TextBox txtAddress;
    private PictureBox picQr;
    private Label lblFirewall;
    private Button btnFirewall;
    private Label lblStatus;
    private Button btnOk;
    private Button btnCancel;
}
