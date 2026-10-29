namespace GrblCore;

public enum SegmentKind
{
    /// <summary>G0 rapid move.</summary>
    Rapid,
    /// <summary>G1/G2/G3 with the laser on - what actually gets burned.</summary>
    Cut,
    /// <summary>G1/G2/G3 with the laser off.</summary>
    Move,
}

/// <summary>Straight piece of the path in work coordinates (mm). Arcs are split into several of these.</summary>
/// <param name="LineIndex">Index into <see cref="GCodeProgram.Lines"/> - the same counter <see cref="GCodeJob.CompletedCount"/> uses.</param>
/// <param name="Feed">Feed rate in effect, mm/min; 0 if the program hasn't set one yet (Grbl then uses its default feed).</param>
public readonly record struct ToolpathSegment(int LineIndex, SegmentKind Kind, double X1, double Y1, double X2, double Y2, double Feed = 0)
{
    public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
}

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

/// <summary>
/// XY toolpath of a program, for drawing. Interprets G0-G3 (I/J and R arcs), G90/G91, G20/G21, G92 and M3/M5.
/// Starts at work position 0,0 - where the job starts on the machine.
/// </summary>
public sealed class Toolpath
{
    /// <summary>Largest allowed distance between an arc and its straight pieces, mm.</summary>
    const double ArcTolerance = 0.01;
    const int MaxArcSegments = 360;

    public IReadOnlyList<ToolpathSegment> Segments { get; }

    /// <summary>Area the laser burns (cuts only); null if nothing is cut.</summary>
    public Bounds? CutBounds { get; }

    /// <summary>Everything including rapids and the start point 0,0.</summary>
    public Bounds AllBounds { get; }

    Toolpath(List<ToolpathSegment> segments)
    {
        Segments = segments;
        CutBounds = BoundsOf(segments.Where(s => s.Kind == SegmentKind.Cut));
        AllBounds = BoundsOf(segments.Append(new ToolpathSegment(-1, SegmentKind.Rapid, 0, 0, 0, 0))) ?? new Bounds(0, 0, 0, 0);
    }

    public static Toolpath Build(GCodeProgram program)
    {
        var segments = new List<ToolpathSegment>();
        double x = 0, y = 0;
        string motion = "G0";
        bool absolute = true, laser = false, xyPlane = true;
        double scale = 1; // mm per unit
        double feed = 0;

        for (int index = 0; index < program.Lines.Count; index++)
        {
            var words = GCodeWord.Parse(program.Lines[index].Text);
            double? nx = null, ny = null, i = null, j = null, r = null, f = null;
            bool setPosition = false, nonMotionCommand = false;

            foreach (var w in words)
            {
                w.TryGetNumber(out var number);
                double value = (double)number;
                switch (w.Letter)
                {
                    case 'G' when w.IsMotion(out var m): motion = m; break;
                    case 'G' when number == 90: absolute = true; break;
                    case 'G' when number == 91: absolute = false; break;
                    case 'G' when number == 20: scale = 25.4; break;
                    case 'G' when number == 21: scale = 1; break;
                    case 'G' when number == 17: xyPlane = true; break;
                    case 'G' when number is 18 or 19: xyPlane = false; break;
                    case 'G' when number == 92: setPosition = true; break;
                    // Axis words on these lines are parameters or machine-coordinate moves we can't place in work coordinates.
                    case 'G' when number is 4 or 10 or 28 or 30 or 53: nonMotionCommand = true; break;
                    case 'M' when number is 3 or 4: laser = true; break;
                    case 'M' when number == 5: laser = false; break;
                    case 'X': nx = value; break;
                    case 'Y': ny = value; break;
                    case 'I': i = value * scale; break;
                    case 'J': j = value * scale; break;
                    case 'R': r = value * scale; break;
                    case 'F': f = value; break;
                }
            }
            // Applied after the loop so "F10 G20" and "G20 F10" both mean inches per minute.
            if (f is { } newFeed)
                feed = newFeed * scale;

            if (nonMotionCommand || (nx is null && ny is null && !(motion is "G2" or "G3" && (i is not null || j is not null))))
                continue;

            if (setPosition)
            {
                // G92 tells Grbl "you are here now" - the tool doesn't move.
                if (nx is { } px) x = px * scale;
                if (ny is { } py) y = py * scale;
                continue;
            }

            double tx = nx is { } ax ? (absolute ? ax * scale : x + ax * scale) : x;
            double ty = ny is { } ay ? (absolute ? ay * scale : y + ay * scale) : y;
            var kind = motion == "G0" ? SegmentKind.Rapid : laser ? SegmentKind.Cut : SegmentKind.Move;

            if (motion is "G2" or "G3" && xyPlane)
                AddArc(segments, index, kind, feed, x, y, tx, ty, i ?? 0, j ?? 0, r, clockwise: motion == "G2");
            else
                segments.Add(new ToolpathSegment(index, kind, x, y, tx, ty, feed));

            x = tx;
            y = ty;
        }
        return new Toolpath(segments);
    }

    static void AddArc(List<ToolpathSegment> segments, int index, SegmentKind kind, double feed,
        double x1, double y1, double x2, double y2, double i, double j, double? r, bool clockwise)
    {
        if (r is { } radius)
        {
            // Center from radius, the same way Grbl does it. Negative R means the arc is longer than 180°.
            double dx = x2 - x1, dy = y2 - y1;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d == 0)
                return;
            double h = -Math.Sqrt(Math.Max(0, 4 * radius * radius - dx * dx - dy * dy)) / d;
            if (!clockwise)
                h = -h;
            if (radius < 0)
                h = -h;
            i = 0.5 * (dx - dy * h);
            j = 0.5 * (dy + dx * h);
        }

        double cx = x1 + i, cy = y1 + j;
        double rad = Math.Sqrt(i * i + j * j);
        double a1 = Math.Atan2(y1 - cy, x1 - cx);
        double a2 = Math.Atan2(y2 - cy, x2 - cx);
        double sweep = a2 - a1;
        bool fullCircle = Math.Abs(x2 - x1) < 1e-9 && Math.Abs(y2 - y1) < 1e-9;
        if (clockwise)
        {
            if (sweep >= 0 || fullCircle) sweep -= 2 * Math.PI;
        }
        else
        {
            if (sweep <= 0 || fullCircle) sweep += 2 * Math.PI;
        }

        int count = 1;
        if (rad > ArcTolerance)
        {
            double maxStep = 2 * Math.Acos(1 - ArcTolerance / rad);
            count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / maxStep), 1, MaxArcSegments);
        }

        double px = x1, py = y1;
        for (int k = 1; k <= count; k++)
        {
            double qx, qy;
            if (k == count)
            {
                (qx, qy) = (x2, y2); // end exactly where the G-code says, not where rounding puts it
            }
            else
            {
                double a = a1 + sweep * k / count;
                (qx, qy) = (cx + rad * Math.Cos(a), cy + rad * Math.Sin(a));
            }
            segments.Add(new ToolpathSegment(index, kind, px, py, qx, qy, feed));
            (px, py) = (qx, qy);
        }
    }

    static Bounds? BoundsOf(IEnumerable<ToolpathSegment> segments)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (var s in segments)
        {
            any = true;
            minX = Math.Min(minX, Math.Min(s.X1, s.X2));
            minY = Math.Min(minY, Math.Min(s.Y1, s.Y2));
            maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
            maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
        }
        return any ? new Bounds(minX, minY, maxX, maxY) : null;
    }
}
