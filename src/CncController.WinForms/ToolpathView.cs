using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GrblCore;

namespace CncController.WinForms;

/// <summary>
/// Top view of the toolpath (work coordinates, Y up) with job progress and the current laser position.
/// Mouse wheel zooms around the cursor, dragging pans, double-click fits the drawing.
/// The path is drawn once into a cached bitmap; progress is added to a second bitmap piece by piece,
/// so large files (100k+ segments) don't get redrawn on every status update.
/// </summary>
sealed class ToolpathView : Control
{
    const float ViewPadding = 24f;
    const double MinScale = 0.05, MaxScale = 500; // px per mm
    const int AntiAliasLimit = 60_000; // above this many segments, drawing quality gives way to speed
    static readonly double[] GridSteps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];

    static readonly Color Background = Color.White;
    static readonly Color GridColor = Color.FromArgb(236, 236, 236);
    static readonly Color AxisColor = Color.FromArgb(190, 190, 190);
    static readonly Color RapidColor = Color.FromArgb(175, 175, 175);
    static readonly Color MoveColor = Color.FromArgb(140, 140, 140);
    static readonly Color CutColor = Color.FromArgb(30, 90, 200);
    static readonly Color DoneColor = Color.FromArgb(235, 120, 0);
    static readonly Color LaserColor = Color.Red;

    Toolpath? _toolpath;
    int _completedLines;
    Position? _laserPosition;

    double _scale = 1, _offsetX, _offsetY; // screen = offset + mm * scale (Y flipped)
    Bitmap? _pathLayer, _doneLayer;
    int _doneSegments; // segments already drawn on _doneLayer

    Point? _dragStart;
    Size _panDelta; // while dragging, cached layers are just shifted; redrawn on mouse up

    public ToolpathView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Background;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Toolpath? Toolpath
    {
        get => _toolpath;
        set
        {
            _toolpath = value;
            _completedLines = 0;
            FitToView();
        }
    }

    /// <summary>Program lines Grbl has accepted; cuts before that are drawn as done.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CompletedLines
    {
        get => _completedLines;
        set
        {
            if (value == _completedLines)
                return;
            bool backwards = value < _completedLines;
            _completedLines = value;
            if (backwards)
                DropLayer(ref _doneLayer); // new job - start the progress layer over
            else
                DrawNewlyDone();
            Invalidate();
        }
    }

    /// <summary>Laser position in work coordinates, or null when unknown.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Position? LaserPosition
    {
        get => _laserPosition;
        set
        {
            if (value == _laserPosition)
                return;
            _laserPosition = value;
            Invalidate();
        }
    }

    public void FitToView()
    {
        var b = _toolpath?.AllBounds ?? new Bounds(0, 0, 100, 100);
        double width = Math.Max(b.Width, 1), height = Math.Max(b.Height, 1);
        double availableW = Math.Max(ClientSize.Width - 2 * ViewPadding, 1);
        double availableH = Math.Max(ClientSize.Height - 2 * ViewPadding - 20, 1); // room for the legend
        _scale = Math.Clamp(Math.Min(availableW / width, availableH / height), MinScale, MaxScale);
        _offsetX = ViewPadding + (availableW - width * _scale) / 2 - b.MinX * _scale;
        _offsetY = ViewPadding + (availableH + height * _scale) / 2 + b.MinY * _scale;
        InvalidateLayers();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitToView();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        double newScale = Math.Clamp(_scale * Math.Pow(1.25, e.Delta / 120.0), MinScale, MaxScale);
        // Keep the point under the cursor in place.
        double mmX = (e.X - _offsetX) / _scale, mmY = (_offsetY - e.Y) / _scale;
        _scale = newScale;
        _offsetX = e.X - mmX * _scale;
        _offsetY = e.Y + mmY * _scale;
        InvalidateLayers();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // so the wheel reaches us
        if (e.Button is MouseButtons.Left or MouseButtons.Middle)
            _dragStart = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is { } start)
        {
            _panDelta = new Size(e.X - start.X, e.Y - start.Y);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart is null)
            return;
        _offsetX += _panDelta.Width;
        _offsetY += _panDelta.Height;
        _panDelta = Size.Empty;
        _dragStart = null;
        InvalidateLayers();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        FitToView();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Background);

        _pathLayer ??= RenderPathLayer();
        if (_doneLayer is null)
        {
            _doneLayer = NewLayer();
            _doneSegments = 0;
            DrawNewlyDone();
        }

        var at = new Point(_panDelta);
        g.DrawImageUnscaled(_pathLayer, at);
        g.DrawImageUnscaled(_doneLayer, at);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawLaser(g);
        DrawLegend(g);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DropLayer(ref _pathLayer);
            DropLayer(ref _doneLayer);
        }
        base.Dispose(disposing);
    }

    void InvalidateLayers()
    {
        DropLayer(ref _pathLayer);
        DropLayer(ref _doneLayer);
        Invalidate();
    }

    static void DropLayer(ref Bitmap? layer)
    {
        layer?.Dispose();
        layer = null;
    }

    Bitmap NewLayer() => new(Math.Max(ClientSize.Width, 1), Math.Max(ClientSize.Height, 1), PixelFormat.Format32bppPArgb);

    PointF ToScreen(double x, double y) => new((float)(_offsetX + x * _scale), (float)(_offsetY - y * _scale));

    Bitmap RenderPathLayer()
    {
        var layer = NewLayer();
        using var g = Graphics.FromImage(layer);
        g.Clear(Background); // text on a transparent bitmap comes out smeared
        DrawGrid(g);
        if (_toolpath is { } t)
        {
            g.SmoothingMode = t.Segments.Count > AntiAliasLimit ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
            using var rapid = new Pen(RapidColor, 1) { DashStyle = DashStyle.Dash };
            using var move = new Pen(MoveColor, 1);
            using var cut = new Pen(CutColor, 1.2f);
            DrawSegments(g, rapid, 0, t.Segments.Count, SegmentKind.Rapid);
            DrawSegments(g, move, 0, t.Segments.Count, SegmentKind.Move);
            DrawSegments(g, cut, 0, t.Segments.Count, SegmentKind.Cut);
        }
        return layer;
    }

    /// <summary>Adds cuts that became done since the last call to the progress layer.</summary>
    void DrawNewlyDone()
    {
        if (_doneLayer is null || _toolpath is not { } t)
            return;
        int done = CountDoneSegments(t);
        if (done <= _doneSegments)
            return;

        using var g = Graphics.FromImage(_doneLayer);
        g.SmoothingMode = t.Segments.Count > AntiAliasLimit ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
        using var pen = new Pen(DoneColor, 2f);
        DrawSegments(g, pen, _doneSegments, done, SegmentKind.Cut);
        _doneSegments = done;
    }

    /// <summary>Segments are ordered by line, so the done ones are a prefix; binary search for its end.</summary>
    int CountDoneSegments(Toolpath t)
    {
        int lo = 0, hi = t.Segments.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (t.Segments[mid].LineIndex < _completedLines)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>Draws segments of one kind, joining connected ones into polylines (much faster than one DrawLine each).</summary>
    void DrawSegments(Graphics g, Pen pen, int from, int to, SegmentKind kind)
    {
        var segments = _toolpath!.Segments;
        var points = new List<PointF>();

        void Flush()
        {
            if (points.Count >= 2)
                g.DrawLines(pen, points.ToArray());
            points.Clear();
        }

        for (int k = from; k < to; k++)
        {
            var s = segments[k];
            if (s.Kind != kind)
            {
                Flush();
                continue;
            }
            var p1 = ToScreen(s.X1, s.Y1);
            if (points.Count == 0 || points[^1] != p1)
            {
                Flush();
                points.Add(p1);
            }
            points.Add(ToScreen(s.X2, s.Y2));
        }
        Flush();
    }

    void DrawGrid(Graphics g)
    {
        // Smallest step that keeps grid lines at least 40 px apart.
        double step = GridSteps.FirstOrDefault(s => s * _scale >= 40, GridSteps[^1]);
        double minX = -_offsetX / _scale, maxX = (ClientSize.Width - _offsetX) / _scale;
        double minY = (_offsetY - ClientSize.Height) / _scale, maxY = _offsetY / _scale;

        using var grid = new Pen(GridColor);
        for (double x = Math.Floor(minX / step) * step; x <= maxX; x += step)
            g.DrawLine(grid, ToScreen(x, minY), ToScreen(x, maxY));
        for (double y = Math.Floor(minY / step) * step; y <= maxY; y += step)
            g.DrawLine(grid, ToScreen(minX, y), ToScreen(maxX, y));

        using var axis = new Pen(AxisColor);
        g.DrawLine(axis, ToScreen(0, minY), ToScreen(0, maxY));
        g.DrawLine(axis, ToScreen(minX, 0), ToScreen(maxX, 0));

        using var brush = new SolidBrush(AxisColor);
        var origin = ToScreen(0, 0);
        g.DrawString("0,0", Font, brush, origin.X + 3, origin.Y + 2);
        g.DrawString($"mreža {step:0.###} mm", Font, brush, 6, 6);
    }

    void DrawLaser(Graphics g)
    {
        if (_laserPosition is not { } pos)
            return;
        var p = ToScreen(pos.X, pos.Y);
        p.X += _panDelta.Width;
        p.Y += _panDelta.Height;
        using var pen = new Pen(LaserColor, 1.5f);
        const float r = 6, arm = 12;
        g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
        g.DrawLine(pen, p.X - arm, p.Y, p.X + arm, p.Y);
        g.DrawLine(pen, p.X, p.Y - arm, p.X, p.Y + arm);
    }

    void DrawLegend(Graphics g)
    {
        float y = ClientSize.Height - 18, x = 8;
        using var text = new SolidBrush(Color.DimGray);

        void Item(Pen pen, string label)
        {
            g.DrawLine(pen, x, y + 7, x + 22, y + 7);
            x += 26;
            g.DrawString(label, Font, text, x, y);
            x += g.MeasureString(label, Font).Width + 12;
        }

        using (var cut = new Pen(CutColor, 2)) Item(cut, "rez");
        using (var done = new Pen(DoneColor, 2)) Item(done, "urađeno");
        using (var rapid = new Pen(RapidColor, 1) { DashStyle = DashStyle.Dash }) Item(rapid, "prazni hod");

        if (_toolpath?.CutBounds is { } b)
            g.DrawString($"rez: {b.Width:0.0} × {b.Height:0.0} mm  (X {b.MinX:0.0} do {b.MaxX:0.0}, Y {b.MinY:0.0} do {b.MaxY:0.0})", Font, text, x + 8, y);

        const string hint = "kotačić: zum · povuci: pomjeri · dupli klik: uklopi";
        var size = g.MeasureString(hint, Font);
        g.DrawString(hint, Font, text, ClientSize.Width - size.Width - 6, 6);
    }
}
