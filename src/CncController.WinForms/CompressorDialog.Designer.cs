namespace CncController.WinForms;

partial class CompressorDialog
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
        lblKnownStart = new Label();
        dtpKnownStart = new DateTimePicker();
        lblPeriod = new Label();
        numPeriod = new NumericUpDown();
        lblMinutes = new Label();
        lblNext = new Label();
        btnClear = new Button();
        btnOk = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)numPeriod).BeginInit();
        SuspendLayout();
        //
        // lblInfo
        //
        lblInfo.Location = new Point(12, 9);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(396, 48);
        lblInfo.Text = "Pauza počinje uvijek u isto vrijeme u ciklusu (npr. kompresori koje pali tajmer). Upiši bilo koje vrijeme kad je počela (npr. vrijeme restarta iz loga) i koliko često se ponavlja.";
        //
        // lblKnownStart
        //
        lblKnownStart.AutoSize = true;
        lblKnownStart.Location = new Point(12, 68);
        lblKnownStart.Name = "lblKnownStart";
        lblKnownStart.Text = "Upalili su se u:";
        //
        // dtpKnownStart
        //
        dtpKnownStart.CustomFormat = "dd.MM.yyyy  HH:mm:ss";
        dtpKnownStart.Format = DateTimePickerFormat.Custom;
        dtpKnownStart.Location = new Point(130, 64);
        dtpKnownStart.Name = "dtpKnownStart";
        dtpKnownStart.ShowUpDown = true;
        dtpKnownStart.Size = new Size(170, 23);
        dtpKnownStart.TabIndex = 0;
        dtpKnownStart.ValueChanged += Inputs_ValueChanged;
        //
        // lblPeriod
        //
        lblPeriod.AutoSize = true;
        lblPeriod.Location = new Point(12, 100);
        lblPeriod.Name = "lblPeriod";
        lblPeriod.Text = "Pale se svakih:";
        //
        // numPeriod
        //
        numPeriod.DecimalPlaces = 1;
        numPeriod.Location = new Point(130, 96);
        numPeriod.Maximum = new decimal(new int[] { 240, 0, 0, 0 });
        // Short periods are for the simulator: at 10x speed its 15 minutes pass in 1.5.
        numPeriod.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
        numPeriod.Name = "numPeriod";
        numPeriod.Size = new Size(70, 23);
        numPeriod.TabIndex = 1;
        numPeriod.Value = new decimal(new int[] { 15, 0, 0, 0 });
        numPeriod.ValueChanged += Inputs_ValueChanged;
        //
        // lblMinutes
        //
        lblMinutes.AutoSize = true;
        lblMinutes.Location = new Point(206, 100);
        lblMinutes.Name = "lblMinutes";
        lblMinutes.Text = "min";
        //
        // lblNext
        //
        lblNext.ForeColor = Color.FromArgb(110, 110, 110);
        lblNext.Location = new Point(12, 132);
        lblNext.Name = "lblNext";
        lblNext.Size = new Size(396, 20);
        //
        // btnClear
        //
        btnClear.Location = new Point(12, 166);
        btnClear.Name = "btnClear";
        btnClear.Size = new Size(110, 30);
        btnClear.TabIndex = 4;
        btnClear.Text = "Obriši ritam";
        btnClear.Click += btnClear_Click;
        //
        // btnOk
        //
        btnOk.Location = new Point(224, 166);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(89, 30);
        btnOk.TabIndex = 2;
        btnOk.Text = "U redu";
        btnOk.Click += btnOk_Click;
        //
        // btnCancel
        //
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(319, 166);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(89, 30);
        btnCancel.TabIndex = 3;
        btnCancel.Text = "Odustani";
        //
        // CompressorDialog
        //
        AcceptButton = btnOk;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(420, 208);
        Controls.Add(lblInfo);
        Controls.Add(lblKnownStart);
        Controls.Add(dtpKnownStart);
        Controls.Add(lblPeriod);
        Controls.Add(numPeriod);
        Controls.Add(lblMinutes);
        Controls.Add(lblNext);
        Controls.Add(btnClear);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "CompressorDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Raspored pauze";
        ((System.ComponentModel.ISupportInitialize)numPeriod).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblInfo;
    private Label lblKnownStart;
    private DateTimePicker dtpKnownStart;
    private Label lblPeriod;
    private NumericUpDown numPeriod;
    private Label lblMinutes;
    private Label lblNext;
    private Button btnClear;
    private Button btnOk;
    private Button btnCancel;
}
