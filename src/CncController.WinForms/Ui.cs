using CncController.Remote;

namespace CncController.WinForms;

/// <summary>Colors and the flat button look shared by all windows.</summary>
static class Ui
{
    public static readonly Color Green = Color.FromArgb(46, 125, 50);
    public static readonly Color Blue = Color.FromArgb(21, 101, 192);
    public static readonly Color Orange = Color.FromArgb(230, 108, 0);
    public static readonly Color Red = Color.FromArgb(198, 40, 40);
    public static readonly Color Grey = Color.FromArgb(117, 117, 117);
    public static readonly Color MutedText = Color.FromArgb(110, 110, 110);

    public static Color Of(Tone tone) => tone switch
    {
        Tone.Green => Green,
        Tone.Blue => Blue,
        Tone.Orange => Orange,
        Tone.Red => Red,
        Tone.Grey => Grey,
        _ => MutedText,
    };

    static readonly Color Border = Color.FromArgb(173, 173, 173);
    static readonly Color Hover = Color.FromArgb(229, 241, 251);
    static readonly Color Pressed = Color.FromArgb(204, 228, 247);
    static readonly Color DisabledBack = Color.FromArgb(230, 230, 230);

    /// <summary>Gives every button under <paramref name="root"/> a flat, light look.</summary>
    public static void FlattenButtons(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Button b)
                Plain(b);
            FlattenButtons(c);
        }
    }

    public static void Plain(Button b)
    {
        b.EnabledChanged -= OnEnabledChanged;
        if (b.Tag is Action)
        {
            b.Tag = null;
            b.Font = new Font(b.Font, FontStyle.Regular);
        }
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.BackColor = Color.White;
        b.ForeColor = SystemColors.ControlText;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = Hover;
        b.FlatAppearance.MouseDownBackColor = Pressed;
        b.Cursor = Cursors.Hand;
    }

    /// <summary>Filled button for the main or dangerous actions; turns grey while disabled.</summary>
    public static void Accent(Button b, Color color)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Font = new Font(b.Font, FontStyle.Bold);
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.25f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, 0.1f);
        b.Cursor = Cursors.Hand;
        void Paint()
        {
            b.BackColor = b.Enabled ? color : DisabledBack;
            b.ForeColor = b.Enabled ? Color.White : Grey;
            b.FlatAppearance.BorderColor = b.Enabled ? color : Border;
        }
        b.EnabledChanged -= OnEnabledChanged;
        b.Tag = (Action)Paint;
        b.EnabledChanged += OnEnabledChanged;
        Paint();
    }

    static void OnEnabledChanged(object? sender, EventArgs e)
    {
        if (sender is Button { Tag: Action paint })
            paint();
    }
}
