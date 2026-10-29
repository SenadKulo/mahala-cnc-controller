namespace CncController.WinForms;

partial class ResumeDialog
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
        lblStopped = new Label();
        lblLine = new Label();
        numLine = new NumericUpDown();
        txtPreview = new TextBox();
        btnResume = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)numLine).BeginInit();
        SuspendLayout();
        //
        // lblInfo
        //
        lblInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblInfo.Location = new Point(12, 9);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(536, 34);
        lblInfo.Text = "Laser se ugasi i ode na mjesto gdje izabrana linija počinje, vrate se brzina i stanje lasera, pa se šalje ostatak fajla. Na slici je preskočeni dio narandžast.";
        //
        // lblStopped
        //
        lblStopped.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblStopped.Location = new Point(12, 47);
        lblStopped.Name = "lblStopped";
        lblStopped.Size = new Size(536, 34);
        lblStopped.ForeColor = Color.DarkBlue;
        //
        // lblLine
        //
        lblLine.AutoSize = true;
        lblLine.Location = new Point(12, 90);
        lblLine.Name = "lblLine";
        lblLine.Text = "Nastavi od linije:";
        //
        // numLine
        //
        numLine.Location = new Point(120, 87);
        numLine.Name = "numLine";
        numLine.Size = new Size(100, 23);
        numLine.TabIndex = 0;
        numLine.ValueChanged += numLine_ValueChanged;
        //
        // txtPreview
        //
        txtPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtPreview.Font = new Font("Consolas", 9F);
        txtPreview.Location = new Point(12, 118);
        txtPreview.Multiline = true;
        txtPreview.Name = "txtPreview";
        txtPreview.ReadOnly = true;
        txtPreview.ScrollBars = ScrollBars.Vertical;
        txtPreview.Size = new Size(536, 230);
        txtPreview.TabStop = false;
        //
        // btnResume
        //
        btnResume.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnResume.DialogResult = DialogResult.OK;
        btnResume.Location = new Point(334, 358);
        btnResume.Name = "btnResume";
        btnResume.Size = new Size(119, 30);
        btnResume.TabIndex = 1;
        btnResume.Text = "Nastavi posao";
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(459, 358);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(89, 30);
        btnCancel.TabIndex = 2;
        btnCancel.Text = "Odustani";
        //
        // ResumeDialog
        //
        AcceptButton = btnResume;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(560, 400);
        Controls.Add(lblInfo);
        Controls.Add(lblStopped);
        Controls.Add(lblLine);
        Controls.Add(numLine);
        Controls.Add(txtPreview);
        Controls.Add(btnResume);
        Controls.Add(btnCancel);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(450, 330);
        Name = "ResumeDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Nastavak posla";
        ((System.ComponentModel.ISupportInitialize)numLine).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblInfo;
    private Label lblStopped;
    private Label lblLine;
    private NumericUpDown numLine;
    private TextBox txtPreview;
    private Button btnResume;
    private Button btnCancel;
}
