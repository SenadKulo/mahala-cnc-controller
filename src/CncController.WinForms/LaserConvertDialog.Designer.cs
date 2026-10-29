namespace CncController.WinForms;

partial class LaserConvertDialog
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
        gridToolpaths = new DataGridView();
        colNumber = new DataGridViewTextBoxColumn();
        colTool = new DataGridViewTextBoxColumn();
        colContours = new DataGridViewTextBoxColumn();
        colPasses = new DataGridViewTextBoxColumn();
        colFeed = new DataGridViewTextBoxColumn();
        btnConvert = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)gridToolpaths).BeginInit();
        SuspendLayout();
        //
        // lblInfo
        //
        lblInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblInfo.Location = new Point(12, 9);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(610, 50);
        lblInfo.Text = "Ovo je originalni ArtCAM fajl. Spuštanje u materijal pali laser (M3), podizanje ga gasi (M5), a Z se briše. Upiši brzinu (feed) za svaku putanju alata.";
        //
        // gridToolpaths
        //
        gridToolpaths.AllowUserToAddRows = false;
        gridToolpaths.AllowUserToDeleteRows = false;
        gridToolpaths.AllowUserToResizeRows = false;
        gridToolpaths.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        gridToolpaths.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridToolpaths.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        gridToolpaths.Columns.AddRange(new DataGridViewColumn[] { colNumber, colTool, colContours, colPasses, colFeed });
        gridToolpaths.Location = new Point(12, 62);
        gridToolpaths.MultiSelect = false;
        gridToolpaths.Name = "gridToolpaths";
        gridToolpaths.RowHeadersVisible = false;
        gridToolpaths.Size = new Size(610, 150);
        gridToolpaths.TabIndex = 0;
        gridToolpaths.CellValidating += gridToolpaths_CellValidating;
        //
        // colNumber
        //
        colNumber.FillWeight = 12F;
        colNumber.HeaderText = "Putanja";
        colNumber.Name = "colNumber";
        colNumber.ReadOnly = true;
        //
        // colTool
        //
        colTool.FillWeight = 50F;
        colTool.HeaderText = "Alat (iz ArtCAM-a)";
        colTool.Name = "colTool";
        colTool.ReadOnly = true;
        //
        // colContours
        //
        colContours.FillWeight = 12F;
        colContours.HeaderText = "Konture";
        colContours.Name = "colContours";
        colContours.ReadOnly = true;
        //
        // colPasses
        //
        colPasses.FillWeight = 12F;
        colPasses.HeaderText = "Prolazi";
        colPasses.Name = "colPasses";
        colPasses.ReadOnly = true;
        //
        // colFeed
        //
        colFeed.FillWeight = 18F;
        colFeed.HeaderText = "Feed (mm/min)";
        colFeed.Name = "colFeed";
        //
        // btnConvert
        //
        btnConvert.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnConvert.Location = new Point(382, 222);
        btnConvert.Name = "btnConvert";
        btnConvert.Size = new Size(145, 30);
        btnConvert.TabIndex = 1;
        btnConvert.Text = "Konvertuj i spremi...";
        btnConvert.Click += btnConvert_Click;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(533, 222);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(89, 30);
        btnCancel.TabIndex = 2;
        btnCancel.Text = "Odustani";
        //
        // LaserConvertDialog
        //
        AcceptButton = btnConvert;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(634, 264);
        Controls.Add(lblInfo);
        Controls.Add(gridToolpaths);
        Controls.Add(btnConvert);
        Controls.Add(btnCancel);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(500, 250);
        Name = "LaserConvertDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Konverzija za laser";
        ((System.ComponentModel.ISupportInitialize)gridToolpaths).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private Label lblInfo;
    private DataGridView gridToolpaths;
    private DataGridViewTextBoxColumn colNumber;
    private DataGridViewTextBoxColumn colTool;
    private DataGridViewTextBoxColumn colContours;
    private DataGridViewTextBoxColumn colPasses;
    private DataGridViewTextBoxColumn colFeed;
    private Button btnConvert;
    private Button btnCancel;
}
