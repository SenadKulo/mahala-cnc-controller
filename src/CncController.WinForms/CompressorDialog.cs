using GrblCore;

namespace CncController.WinForms;

/// <summary>Shows and edits when the compressors switch on: one known moment and the period.</summary>
public partial class CompressorDialog : Form
{
    /// <summary>Valid after the dialog returns OK; null if the rhythm was cleared.</summary>
    public CompressorSchedule? Schedule { get; private set; }

    public CompressorDialog(CompressorSchedule? current)
    {
        InitializeComponent();
        Ui.FlattenButtons(this);
        Ui.Accent(btnOk, Ui.Blue);

        dtpKnownStart.Value = current?.KnownStart ?? DateTime.Now;
        numPeriod.Value = (decimal)Math.Clamp((current?.Period ?? CompressorSchedule.DefaultPeriod).TotalMinutes, (double)numPeriod.Minimum, (double)numPeriod.Maximum);
        btnClear.Enabled = current is not null;
        UpdateNext();
    }

    void Inputs_ValueChanged(object? sender, EventArgs e) => UpdateNext();

    CompressorSchedule Entered => new(dtpKnownStart.Value, TimeSpan.FromMinutes((double)numPeriod.Value));

    void UpdateNext()
    {
        var next = Entered.NextStart(DateTime.Now);
        lblNext.Text = $"Sljedeće paljenje: {next:HH:mm:ss}, pa {next + Entered.Period:HH:mm:ss}...";
    }

    void btnOk_Click(object sender, EventArgs e)
    {
        Schedule = Entered;
        DialogResult = DialogResult.OK;
    }

    void btnClear_Click(object sender, EventArgs e)
    {
        Schedule = null;
        DialogResult = DialogResult.OK;
    }
}
