using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using GrblCore;

namespace CncController.WinForms;

/// <summary>
/// Moving the axes by a chosen step: X/Y arrows around a centre that shows the step, Z beside them,
/// the step as a segmented switch, optionally driven by the arrow keys. Looks like the web page's (site.css, .jog).
/// </summary>
sealed class JogControl : GroupBox
{
    // Tile size and gap; the row of buttons the window adds below starts at TilesBottom + 8.
    const int Tile = 50, TileHeight = 40, Gap = 4, Left0 = 10, Top0 = 22;
    public const int TilesBottom = Top0 + 3 * TileHeight + 2 * Gap;

    static readonly Color XyColor = Ui.Blue;
    static readonly Color ZColor = Color.FromArgb(94, 53, 177);

    readonly List<RadioButton> _stepButtons = [];
    readonly JogTile _center;
    readonly CheckBox _chkKeyboard;

    /// <summary>Raised with the axis and signed distance in mm.</summary>
    public event Action<Axis, double>? JogRequested;

    /// <summary>Raised when the user picks another step.</summary>
    public event Action<double>? StepChanged;

    public JogControl()
    {
        Text = "Ručno upravljanje";
        Height = 200;

        int Col(int i) => Left0 + i * (Tile + Gap);
        int Row(int i) => Top0 + i * (TileHeight + Gap);
        AddJogTile("Y+", ArrowDirection.Up, Axis.Y, +1, Col(1), Row(0), XyColor);
        AddJogTile("X−", ArrowDirection.Left, Axis.X, -1, Col(0), Row(1), XyColor);
        AddJogTile("X+", ArrowDirection.Right, Axis.X, +1, Col(2), Row(1), XyColor);
        AddJogTile("Y−", ArrowDirection.Down, Axis.Y, -1, Col(1), Row(2), XyColor);
        _center = new JogTile { Location = new Point(Col(1), Row(1)), Size = new Size(Tile, TileHeight), Dark = true };
        Controls.Add(_center);

        int zLeft = Col(3) + 12;
        AddJogTile("Z+", ArrowDirection.Up, Axis.Z, +1, zLeft, Row(0), ZColor);
        Controls.Add(new Label
        {
            Text = "Z",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = ZColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(zLeft, Row(1)),
            Size = new Size(Tile, TileHeight),
        });
        AddJogTile("Z−", ArrowDirection.Down, Axis.Z, -1, zLeft, Row(2), ZColor);

        int stepsLeft = zLeft + Tile + 22;
        Controls.Add(new Label { Text = "Korak (mm)", AutoSize = true, ForeColor = Ui.MutedText, Location = new Point(stepsLeft, Top0 + 2) });
        int x = stepsLeft;
        foreach (var step in Jog.Steps)
        {
            // A segmented switch: the chosen step stays lit.
            var radio = new RadioButton
            {
                Text = step.ToString(CultureInfo.InvariantCulture),
                Tag = step,
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, Top0 + 22),
                Size = new Size(45, 32),
                Cursor = Cursors.Hand,
                BackColor = Color.White,
            };
            radio.FlatAppearance.BorderColor = Color.FromArgb(173, 173, 173);
            radio.FlatAppearance.CheckedBackColor = Ui.Blue;
            radio.CheckedChanged += (_, _) =>
            {
                radio.ForeColor = radio.Checked ? Color.White : SystemColors.ControlText;
                if (!radio.Checked)
                    return;
                _center.Caption = $"{radio.Text} mm";
                StepChanged?.Invoke(step);
            };
            _stepButtons.Add(radio);
            Controls.Add(radio);
            // Touching, like one control.
            x += 44;
        }
        _stepButtons[1].Checked = true;

        _chkKeyboard = new CheckBox
        {
            Text = "Strelice na tastaturi",
            AutoSize = true,
            Location = new Point(stepsLeft, Top0 + 66),
        };
        new ToolTip().SetToolTip(_chkKeyboard, "Kad je uključeno, strelice pomjeraju X/Y, a PgUp/PgDn Z, za jedan korak po pritisku (držanje tipke ne ponavlja).");
        Controls.Add(_chkKeyboard);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Step
    {
        get => (double)_stepButtons.First(r => r.Checked).Tag!;
        set => (_stepButtons.FirstOrDefault(r => (double)r.Tag! == value) ?? _stepButtons[1]).Checked = true;
    }

    /// <summary>
    /// Handles a jog key if keyboard jogging is on. Returns true if the key was a jog key.
    /// An auto-repeated key is swallowed without moving.
    /// </summary>
    public bool HandleKey(Keys key, bool repeat)
    {
        if (!_chkKeyboard.Checked)
            return false;
        (Axis Axis, int Sign)? jog = key switch
        {
            Keys.Left => (Axis.X, -1),
            Keys.Right => (Axis.X, +1),
            Keys.Up => (Axis.Y, +1),
            Keys.Down => (Axis.Y, -1),
            Keys.PageUp => (Axis.Z, +1),
            Keys.PageDown => (Axis.Z, -1),
            _ => null,
        };
        if (jog is not { } j)
            return false;
        if (Enabled && !repeat)
            JogRequested?.Invoke(j.Axis, j.Sign * Step);
        return true;
    }

    void AddJogTile(string caption, ArrowDirection arrow, Axis axis, int sign, int x, int y, Color color)
    {
        var tile = new JogTile
        {
            Caption = caption,
            Arrow = arrow,
            Accent = color,
            Location = new Point(x, y),
            Size = new Size(Tile, TileHeight),
        };
        tile.Click += (_, _) => JogRequested?.Invoke(axis, sign * Step);
        Controls.Add(tile);
    }
}

/// <summary>A rounded key with an arrow and a short caption, drawn by itself so it looks the same everywhere.</summary>
sealed class JogTile : Control
{
    bool _hover, _pressed, _dark;
    string _caption = "";

    public JogTile()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        Cursor = Cursors.Hand;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    /// <summary>Which way the arrow points; <see cref="ArrowDirection"/> values other than the four draw none.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ArrowDirection Arrow { get; set; } = (ArrowDirection)(-1);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; set; } = Ui.Blue;

    /// <summary>Not a key: the dark centre that shows the step, like the position display.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dark
    {
        get => _dark;
        set { _dark = value; Cursor = value ? Cursors.Default : Cursors.Hand; }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled && !Dark)
            base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var shape = Rounded(bounds, 7);

        Color back, fore, border;
        if (Dark)
            (back, fore, border) = (Color.FromArgb(32, 36, 42), Color.FromArgb(236, 240, 241), Color.FromArgb(32, 36, 42));
        else if (!Enabled)
            (back, fore, border) = (Color.FromArgb(240, 240, 240), Color.FromArgb(170, 170, 170), Color.FromArgb(220, 220, 220));
        else
        {
            // Light tint of the accent; stronger on hover, filled while pressed.
            back = _pressed ? Accent : Blend(Accent, _hover ? 0.22f : 0.10f);
            fore = _pressed ? Color.White : Accent;
            border = Blend(Accent, 0.35f);
        }
        using (var fill = new SolidBrush(back))
            g.FillPath(fill, shape);
        using (var pen = new Pen(border))
            g.DrawPath(pen, shape);
        // Arrow on top, caption under it; the centre only shows its caption.
        bool hasArrow = Arrow is ArrowDirection.Up or ArrowDirection.Down or ArrowDirection.Left or ArrowDirection.Right;
        var captionBox = hasArrow ? new Rectangle(0, Height / 2, Width, Height / 2 - 2) : ClientRectangle;
        if (hasArrow)
        {
            using var brush = new SolidBrush(fore);
            g.FillPolygon(brush, Triangle(new PointF(Width / 2f, Height * 0.30f), 6.5f, Arrow));
        }
        TextRenderer.DrawText(g, _caption, Font, captionBox, fore,
            TextFormatFlags.HorizontalCenter | (hasArrow ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter) | TextFormatFlags.NoPadding);
    }

    static Color Blend(Color color, float amount) => Color.FromArgb(
        (int)(255 + (color.R - 255) * amount), (int)(255 + (color.G - 255) * amount), (int)(255 + (color.B - 255) * amount));

    static PointF[] Triangle(PointF c, float r, ArrowDirection direction) => direction switch
    {
        ArrowDirection.Up => [new(c.X, c.Y - r), new(c.X + r, c.Y + r * 0.6f), new(c.X - r, c.Y + r * 0.6f)],
        ArrowDirection.Down => [new(c.X, c.Y + r), new(c.X + r, c.Y - r * 0.6f), new(c.X - r, c.Y - r * 0.6f)],
        ArrowDirection.Left => [new(c.X - r, c.Y), new(c.X + r * 0.6f, c.Y - r), new(c.X + r * 0.6f, c.Y + r)],
        _ => [new(c.X + r, c.Y), new(c.X - r * 0.6f, c.Y - r), new(c.X - r * 0.6f, c.Y + r)],
    };

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
