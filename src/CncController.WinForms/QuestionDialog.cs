using CncController.App;

namespace CncController.WinForms;

/// <summary>
/// Asks the session's position question. Unlike a message box it closes by itself
/// when the question goes away, e.g. answered from another screen or the connection dropped.
/// </summary>
public partial class QuestionDialog : Form
{
    public PositionQuestion Question { get; }

    public QuestionDialog(PositionQuestion question, string title, Language language)
    {
        InitializeComponent();
        Ui.FlattenButtons(this);
        Ui.Accent(btnYes, Ui.Blue);
        Question = question;
        Text = title;
        lblQuestion.Text = Texts.Question(question, language);
        btnYes.Text = language.T("Da", "Yes");
        btnNo.Text = language.T("Ne", "No");
    }

    /// <summary>Closes without an answer if <paramref name="pending"/> is no longer this question.</summary>
    public void CloseIfGone(PositionQuestion? pending)
    {
        if (pending != Question && Visible)
            DialogResult = DialogResult.Cancel;
    }
}
