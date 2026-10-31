namespace CncController.WinForms;

partial class TermsDialog
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
        txtTerms = new TextBox();
        chkAccept = new CheckBox();
        chkUpdates = new CheckBox();
        btnAccept = new Button();
        btnDecline = new Button();
        SuspendLayout();
        //
        // txtTerms
        //
        txtTerms.BackColor = SystemColors.Window;
        txtTerms.Location = new Point(12, 12);
        txtTerms.Multiline = true;
        txtTerms.Name = "txtTerms";
        txtTerms.ReadOnly = true;
        txtTerms.ScrollBars = ScrollBars.Vertical;
        txtTerms.Size = new Size(536, 420);
        txtTerms.TabStop = false;
        //
        // chkAccept
        //
        chkAccept.AutoSize = true;
        chkAccept.Location = new Point(12, 444);
        chkAccept.Name = "chkAccept";
        chkAccept.TabIndex = 0;
        chkAccept.Text = "Pročitao sam ovo i prihvatam";
        chkAccept.CheckedChanged += chkAccept_CheckedChanged;
        //
        // chkUpdates
        //
        chkUpdates.AutoSize = true;
        chkUpdates.Location = new Point(12, 469);
        chkUpdates.Name = "chkUpdates";
        chkUpdates.TabIndex = 1;
        chkUpdates.Text = "Provjeravaj nove verzije (mahalaworkshop.com)";
        //
        // btnAccept
        //
        btnAccept.DialogResult = DialogResult.OK;
        btnAccept.Enabled = false;
        btnAccept.Location = new Point(364, 476);
        btnAccept.Name = "btnAccept";
        btnAccept.Size = new Size(89, 30);
        btnAccept.TabIndex = 2;
        btnAccept.Text = "Prihvatam";
        //
        // btnDecline
        //
        btnDecline.DialogResult = DialogResult.Cancel;
        btnDecline.Location = new Point(459, 476);
        btnDecline.Name = "btnDecline";
        btnDecline.Size = new Size(89, 30);
        btnDecline.TabIndex = 3;
        btnDecline.Text = "Odustani";
        //
        // TermsDialog
        //
        AcceptButton = btnAccept;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnDecline;
        ClientSize = new Size(560, 518);
        Controls.Add(txtTerms);
        Controls.Add(chkAccept);
        Controls.Add(chkUpdates);
        Controls.Add(btnAccept);
        Controls.Add(btnDecline);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "TermsDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Uslovi korištenja i sigurnost lasera";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TextBox txtTerms;
    private CheckBox chkAccept;
    private CheckBox chkUpdates;
    private Button btnAccept;
    private Button btnDecline;
}
