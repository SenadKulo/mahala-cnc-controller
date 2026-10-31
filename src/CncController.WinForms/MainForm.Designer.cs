namespace CncController.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _page?.Dispose();
            _remote.Dispose();
            _session.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        // The page's background (--bg in site.css), so the window doesn't flash white while the page loads.
        BackColor = Color.FromArgb(17, 24, 33);
        ClientSize = new Size(1300, 782);
        MinimumSize = new Size(980, 742);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "The Mahala Workshop - CNC Controller";
        FormClosing += MainForm_FormClosing;
        ResumeLayout(false);
    }

    #endregion
}
