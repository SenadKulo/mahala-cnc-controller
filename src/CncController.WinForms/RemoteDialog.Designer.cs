namespace CncController.WinForms;

partial class RemoteDialog
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
        lblInfo = new Label();
        chkEnabled = new CheckBox();
        lblUrl = new Label();
        txtUrl = new TextBox();
        lblKey = new Label();
        txtKey = new TextBox();
        chkShowKey = new CheckBox();
        lblStatus = new Label();
        btnOk = new Button();
        btnCancel = new Button();
        SuspendLayout();
        //
        // lblInfo
        //
        lblInfo.Location = new Point(12, 9);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(436, 48);
        lblInfo.Text = "Program se spaja na web stranicu, pa se CNC može pratiti i zaustaviti sa mobitela. Program radi isto i kad stranica ili internet nisu dostupni.";
        //
        // chkEnabled
        //
        chkEnabled.AutoSize = true;
        chkEnabled.Location = new Point(12, 62);
        chkEnabled.Name = "chkEnabled";
        chkEnabled.TabIndex = 0;
        chkEnabled.Text = "Uključi daljinski pristup";
        //
        // lblUrl
        //
        lblUrl.AutoSize = true;
        lblUrl.Location = new Point(12, 96);
        lblUrl.Name = "lblUrl";
        lblUrl.Text = "Adresa stranice:";
        //
        // txtUrl
        //
        txtUrl.Location = new Point(130, 92);
        txtUrl.Name = "txtUrl";
        txtUrl.PlaceholderText = "https://...";
        txtUrl.Size = new Size(318, 23);
        txtUrl.TabIndex = 1;
        //
        // lblKey
        //
        lblKey.AutoSize = true;
        lblKey.Location = new Point(12, 128);
        lblKey.Name = "lblKey";
        lblKey.Text = "Ključ:";
        //
        // txtKey
        //
        txtKey.Location = new Point(130, 124);
        txtKey.Name = "txtKey";
        txtKey.Size = new Size(318, 23);
        txtKey.TabIndex = 2;
        txtKey.UseSystemPasswordChar = true;
        //
        // chkShowKey
        //
        chkShowKey.AutoSize = true;
        chkShowKey.Location = new Point(130, 152);
        chkShowKey.Name = "chkShowKey";
        chkShowKey.TabIndex = 3;
        chkShowKey.Text = "Prikaži ključ";
        chkShowKey.CheckedChanged += chkShowKey_CheckedChanged;
        //
        // lblStatus
        //
        lblStatus.ForeColor = Color.FromArgb(110, 110, 110);
        lblStatus.Location = new Point(12, 182);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(436, 36);
        //
        // btnOk
        //
        btnOk.Location = new Point(264, 226);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(89, 30);
        btnOk.TabIndex = 4;
        btnOk.Text = "U redu";
        btnOk.Click += btnOk_Click;
        //
        // btnCancel
        //
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(359, 226);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(89, 30);
        btnCancel.TabIndex = 5;
        btnCancel.Text = "Odustani";
        //
        // RemoteDialog
        //
        AcceptButton = btnOk;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(460, 268);
        Controls.Add(lblInfo);
        Controls.Add(chkEnabled);
        Controls.Add(lblUrl);
        Controls.Add(txtUrl);
        Controls.Add(lblKey);
        Controls.Add(txtKey);
        Controls.Add(chkShowKey);
        Controls.Add(lblStatus);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "RemoteDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Daljinski pristup";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblInfo;
    private CheckBox chkEnabled;
    private Label lblUrl;
    private TextBox txtUrl;
    private Label lblKey;
    private TextBox txtKey;
    private CheckBox chkShowKey;
    private Label lblStatus;
    private Button btnOk;
    private Button btnCancel;
}
