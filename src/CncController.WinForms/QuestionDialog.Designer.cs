namespace CncController.WinForms;

partial class QuestionDialog
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
        lblQuestion = new Label();
        btnYes = new Button();
        btnNo = new Button();
        SuspendLayout();
        //
        // lblQuestion
        //
        lblQuestion.Location = new Point(12, 12);
        lblQuestion.Name = "lblQuestion";
        lblQuestion.Size = new Size(436, 190);
        //
        // btnYes
        //
        btnYes.DialogResult = DialogResult.Yes;
        btnYes.Location = new Point(264, 212);
        btnYes.Name = "btnYes";
        btnYes.Size = new Size(89, 30);
        btnYes.TabIndex = 0;
        btnYes.Text = "Da";
        //
        // btnNo
        //
        btnNo.DialogResult = DialogResult.No;
        btnNo.Location = new Point(359, 212);
        btnNo.Name = "btnNo";
        btnNo.Size = new Size(89, 30);
        btnNo.TabIndex = 1;
        btnNo.Text = "Ne";
        //
        // QuestionDialog
        //
        AcceptButton = btnYes;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(460, 254);
        ControlBox = false;
        Controls.Add(lblQuestion);
        Controls.Add(btnYes);
        Controls.Add(btnNo);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "QuestionDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "CNC Controller";
        ResumeLayout(false);
    }

    #endregion

    private Label lblQuestion;
    private Button btnYes;
    private Button btnNo;
}
