namespace CncController.WinForms;

partial class GrblSettingsDialog
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
        gridSettings = new DataGridView();
        colNumber = new DataGridViewTextBoxColumn();
        colValue = new DataGridViewTextBoxColumn();
        colDescription = new DataGridViewTextBoxColumn();
        colGrblText = new DataGridViewTextBoxColumn();
        lblStatus = new Label();
        btnReload = new Button();
        btnExport = new Button();
        btnSave = new Button();
        btnClose = new Button();
        ((System.ComponentModel.ISupportInitialize)gridSettings).BeginInit();
        SuspendLayout();
        //
        // lblInfo
        //
        lblInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblInfo.Location = new Point(12, 9);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(860, 36);
        lblInfo.Text = "Postavke se čuvaju u Grbl-u (EEPROM) i ostaju i nakon gašenja. Promijeni vrijednost u koloni Vrijednost, pa klikni Spremi izmjene. Prije većih izmjena napravi Izvoz da imaš staru verziju.";
        //
        // gridSettings
        //
        gridSettings.AllowUserToAddRows = false;
        gridSettings.AllowUserToDeleteRows = false;
        gridSettings.AllowUserToResizeRows = false;
        gridSettings.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        gridSettings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridSettings.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        gridSettings.Columns.AddRange(new DataGridViewColumn[] { colNumber, colValue, colDescription, colGrblText });
        gridSettings.Location = new Point(12, 48);
        gridSettings.MultiSelect = false;
        gridSettings.Name = "gridSettings";
        gridSettings.RowHeadersVisible = false;
        gridSettings.Size = new Size(860, 452);
        gridSettings.TabIndex = 0;
        gridSettings.CellValidating += gridSettings_CellValidating;
        gridSettings.CellEndEdit += gridSettings_CellEndEdit;
        //
        // colNumber
        //
        colNumber.FillWeight = 6F;
        colNumber.HeaderText = "$";
        colNumber.Name = "colNumber";
        colNumber.ReadOnly = true;
        //
        // colValue
        //
        colValue.FillWeight = 14F;
        colValue.HeaderText = "Vrijednost";
        colValue.Name = "colValue";
        //
        // colDescription
        //
        colDescription.FillWeight = 50F;
        colDescription.HeaderText = "Opis";
        colDescription.Name = "colDescription";
        colDescription.ReadOnly = true;
        //
        // colGrblText
        //
        colGrblText.FillWeight = 30F;
        colGrblText.HeaderText = "Grbl opis";
        colGrblText.Name = "colGrblText";
        colGrblText.ReadOnly = true;
        //
        // lblStatus
        //
        lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lblStatus.AutoEllipsis = true;
        lblStatus.Location = new Point(12, 517);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(420, 18);
        lblStatus.Text = "";
        //
        // btnReload
        //
        btnReload.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnReload.Location = new Point(438, 510);
        btnReload.Name = "btnReload";
        btnReload.Size = new Size(105, 30);
        btnReload.TabIndex = 1;
        btnReload.Text = "Učitaj ponovo";
        btnReload.Click += btnReload_Click;
        //
        // btnExport
        //
        btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnExport.Location = new Point(549, 510);
        btnExport.Name = "btnExport";
        btnExport.Size = new Size(95, 30);
        btnExport.TabIndex = 2;
        btnExport.Text = "Izvoz...";
        btnExport.Click += btnExport_Click;
        //
        // btnSave
        //
        btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnSave.Location = new Point(650, 510);
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(125, 30);
        btnSave.TabIndex = 3;
        btnSave.Text = "Spremi izmjene";
        btnSave.Click += btnSave_Click;
        //
        // btnClose
        //
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnClose.DialogResult = DialogResult.Cancel;
        btnClose.Location = new Point(781, 510);
        btnClose.Name = "btnClose";
        btnClose.Size = new Size(91, 30);
        btnClose.TabIndex = 4;
        btnClose.Text = "Zatvori";
        //
        // GrblSettingsDialog
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(884, 552);
        Controls.Add(lblInfo);
        Controls.Add(gridSettings);
        Controls.Add(lblStatus);
        Controls.Add(btnReload);
        Controls.Add(btnExport);
        Controls.Add(btnSave);
        Controls.Add(btnClose);
        MinimizeBox = false;
        MinimumSize = new Size(700, 350);
        Name = "GrblSettingsDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Grbl postavke";
        FormClosing += GrblSettingsDialog_FormClosing;
        ((System.ComponentModel.ISupportInitialize)gridSettings).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private Label lblInfo;
    private DataGridView gridSettings;
    private DataGridViewTextBoxColumn colNumber;
    private DataGridViewTextBoxColumn colValue;
    private DataGridViewTextBoxColumn colDescription;
    private DataGridViewTextBoxColumn colGrblText;
    private Label lblStatus;
    private Button btnReload;
    private Button btnExport;
    private Button btnSave;
    private Button btnClose;
}
