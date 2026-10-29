using CncController.App;
using GrblCore;

namespace CncController.WinForms;

/// <summary>Asks from which line to continue a job and shows what will be sent first.</summary>
public partial class ResumeDialog : Form
{
    readonly GCodeProgram _program;
    readonly Position _currentWork;
    readonly JobEstimate? _estimate;

    /// <summary>Valid after the dialog returns OK.</summary>
    public ResumePlan? Plan { get; private set; }

    /// <summary>Raised with the index of the line the job would start at, to preview it; -1 when it can't start.</summary>
    public event Action<int>? StartChanged;

    /// <param name="suggestedIndex">Line index to offer first.</param>
    /// <param name="stoppedText">Explains where the interrupted job stopped, or null if there is none for this file.</param>
    public ResumeDialog(GCodeProgram program, Position currentWork, JobEstimate? estimate, int suggestedIndex, string? stoppedText)
    {
        InitializeComponent();
        Ui.FlattenButtons(this);
        Ui.Accent(btnResume, Ui.Green);
        _program = program;
        _currentWork = currentWork;
        _estimate = estimate;

        numLine.Minimum = 1;
        numLine.Maximum = program.Lines[^1].SourceLineNumber;
        numLine.Value = program.Lines[Math.Clamp(suggestedIndex, 0, program.Lines.Count - 1)].SourceLineNumber;
        lblStopped.Text = stoppedText ?? "Nema zapamćenog prekida za ovaj fajl - izaberi liniju sam.";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdatePlan();
    }

    void numLine_ValueChanged(object? sender, EventArgs e) => UpdatePlan();

    void UpdatePlan()
    {
        int sourceLine = (int)numLine.Value;
        int index = 0;
        while (index < _program.Lines.Count - 1 && _program.Lines[index].SourceLineNumber < sourceLine)
            index++;

        if (!JobResume.TryPlan(_program, index, _currentWork, out var plan, out var problem))
        {
            Plan = null;
            btnResume.Enabled = false;
            txtPreview.Text = Texts.ResumeProblem(problem, Language.Bosnian);
            StartChanged?.Invoke(-1);
            return;
        }

        Plan = plan;
        btnResume.Enabled = true;
        txtPreview.Text = Texts.ResumePlan(_program, plan, _estimate, Language.Bosnian);
        StartChanged?.Invoke(plan.StartIndex);
    }
}
