using CncController.App;
using GrblCore;

namespace CncController.WinForms;

/// <summary>Shows the toolpaths of an ArtCAM file and asks for a feed rate per toolpath.</summary>
public partial class LaserConvertDialog : Form
{
    const int MaxFeed = 10_000;

    readonly Language _language;

    /// <summary>Chosen feed per toolpath, valid after the dialog returns OK.</summary>
    public IReadOnlyList<int> FeedRates { get; private set; } = [];

    public LaserConvertDialog(string fileName, IReadOnlyList<ArtCamToolpath> toolpaths, int firstFeed, int otherFeed, Language language)
    {
        InitializeComponent();
        _language = language;
        if (language == Language.English)
        {
            lblInfo.Text = "This is an original ArtCAM file. Plunging into the material switches the laser on (M3), lifting switches it off (M5), and Z is removed. Enter a speed (feed) for each toolpath.";
            colNumber.HeaderText = "Toolpath";
            colTool.HeaderText = "Tool (from ArtCAM)";
            colContours.HeaderText = "Contours";
            colPasses.HeaderText = "Passes";
            btnConvert.Text = "Convert and save...";
            btnCancel.Text = "Cancel";
        }
        Ui.FlattenButtons(this);
        Ui.Accent(btnConvert, Ui.Blue);
        Text = language.T("Konverzija za laser - ", "Conversion for the laser - ") + fileName;

        // A file with Z moves but no "M3 S" still converts as one toolpath.
        if (toolpaths.Count == 0)
            toolpaths = [new ArtCamToolpath(1, null, 0, 0)];

        foreach (var t in toolpaths)
            gridToolpaths.Rows.Add(t.Number, t.Tool ?? "-", t.Contours, t.Passes, t.Number == 1 ? firstFeed : otherFeed);
    }

    void gridToolpaths_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != colFeed.Index)
            return;
        bool valid = int.TryParse(e.FormattedValue?.ToString(), out var feed) && feed > 0 && feed <= MaxFeed;
        gridToolpaths.Rows[e.RowIndex].ErrorText = valid ? ""
            : _language.T($"Feed mora biti cijeli broj od 1 do {MaxFeed}.", $"The feed must be a whole number from 1 to {MaxFeed}.");
        e.Cancel = !valid;
    }

    void btnConvert_Click(object sender, EventArgs e)
    {
        if (!ValidateChildren() || gridToolpaths.IsCurrentCellDirty && !gridToolpaths.EndEdit())
            return;
        FeedRates = gridToolpaths.Rows.Cast<DataGridViewRow>().Select(r => Convert.ToInt32(r.Cells[colFeed.Index].Value)).ToList();
        DialogResult = DialogResult.OK;
    }
}
