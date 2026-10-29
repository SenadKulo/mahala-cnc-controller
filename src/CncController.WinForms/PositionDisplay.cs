using System.ComponentModel;
using System.Globalization;
using GrblCore;

namespace CncController.WinForms;

/// <summary>Digital readout: large work position per axis, with the machine position small underneath.</summary>
sealed class PositionDisplay : Control
{
    static readonly Color Background = Color.FromArgb(32, 36, 42);
    static readonly Color Separator = Color.FromArgb(58, 64, 72);
    static readonly Color AxisColor = Color.FromArgb(138, 180, 248);
    static readonly Color ValueColor = Color.FromArgb(236, 240, 241);
    static readonly Color UnconfirmedColor = Color.FromArgb(255, 183, 77);
    static readonly Color MachineColor = Color.FromArgb(140, 148, 158);

    readonly Font _axisFont = new("Segoe UI", 10F, FontStyle.Bold);
    readonly Font _valueFont = new("Consolas", 17F, FontStyle.Bold);
    readonly Font _machineFont = new("Consolas", 8F);

    Position? _work, _machine;
    bool _confirmed = true;

    public PositionDisplay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Background;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Position? Work
    {
        get => _work;
        set { if (_work != value) { _work = value; Invalidate(); } }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Position? Machine
    {
        get => _machine;
        set { if (_machine != value) { _machine = value; Invalidate(); } }
    }

    /// <summary>False while the work position may be wrong (after a restart, before it is checked); the numbers turn amber.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Confirmed
    {
        get => _confirmed;
        set { if (_confirmed != value) { _confirmed = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        int columnWidth = Width / 3;
        using var separator = new Pen(Separator);
        for (int i = 0; i < 3; i++)
        {
            var column = new Rectangle(i * columnWidth, 0, i == 2 ? Width - 2 * columnWidth : columnWidth, Height);
            if (i > 0)
                g.DrawLine(separator, column.Left, 6, column.Left, Height - 6);
            DrawAxis(g, column, "XYZ"[i], Get(_work, i), Get(_machine, i));
        }
    }

    void DrawAxis(Graphics g, Rectangle column, char axis, double? work, double? machine)
    {
        const int Pad = 8;
        TextRenderer.DrawText(g, axis.ToString(), _axisFont, new Point(column.Left + Pad, 5), AxisColor);

        var valueBox = new Rectangle(column.Left + Pad, 4, column.Width - 2 * Pad, 34);
        TextRenderer.DrawText(g, Format(work), _valueFont, valueBox,
            work is null ? MachineColor : _confirmed ? ValueColor : UnconfirmedColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var machineBox = new Rectangle(column.Left + Pad, 40, column.Width - 2 * Pad, 18);
        TextRenderer.DrawText(g, machine is null ? "" : "apsolutna " + Format(machine), _machineFont, machineBox, MachineColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    static double? Get(Position? p, int axis) => p is not { } v ? null : axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    // Rounded first so -0.0001 shows as 0.000, like Position.ToString.
    static string Format(double? value) =>
        value is { } v ? (Math.Round(v, 3) + 0.0).ToString("0.000", CultureInfo.InvariantCulture) : "—";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _axisFont.Dispose();
            _valueFont.Dispose();
            _machineFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
