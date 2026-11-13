namespace GrblCore;

/// <summary>
/// Whether a running job has the laser lit. Grbl doesn't report it, and "ok" only means a line entered the planner,
/// so the line being executed is found from the machine's position: the segment nearest to it among the lines
/// accepted last. The laser is lit on that line if the program's M3/M4 is in effect there.
/// </summary>
public sealed class JobLaser
{
    /// <summary>Accepted lines that can still wait in Grbl's planner (18 blocks in 0.8c, 15-16 in 1.1), with a margin.</summary>
    public const int LinesInPlanner = 24;

    readonly bool[] _onAfter;
    readonly IReadOnlyList<ToolpathSegment> _segments;
    int _executing;

    public JobLaser(GCodeProgram program, Toolpath toolpath, int startIndex = 0)
    {
        _onAfter = new bool[program.Lines.Count];
        bool on = false;
        for (int i = 0; i < program.Lines.Count; i++)
        {
            foreach (var w in GCodeWord.Parse(program.Lines[i].Text))
            {
                if (w.Is('M', 3) || w.Is('M', 4))
                    on = true;
                else if (w.Is('M', 5))
                    on = false;
            }
            _onAfter[i] = on;
        }
        _segments = toolpath.Segments;
        _executing = Math.Max(0, startIndex - 1);
    }

    /// <summary>The program's M3/M4 is in effect after the line with this index.</summary>
    public bool IsOnAfter(int lineIndex) => lineIndex >= 0 && lineIndex < _onAfter.Length && _onAfter[lineIndex];

    /// <summary>
    /// Index of the line the machine executes now: of the lines from the last one found up to the last accepted one,
    /// the first whose path passes nearest to <paramref name="work"/>. It never goes back, so a spot the path
    /// passes twice doesn't make it jump.
    /// </summary>
    /// <param name="completed">Lines Grbl accepted (<see cref="GCodeJob.CompletedCount"/>).</param>
    /// <param name="kind">The kind of that line's segment, null if it has none (e.g. a line with only M3).</param>
    public int ExecutingLine(int completed, Position work, out SegmentKind? kind)
    {
        kind = null;
        int last = completed - 1;
        if (last < 0)
            return -1;
        int first = Math.Max(_executing, last - LinesInPlanner);
        _executing = Math.Min(Math.Max(_executing, first), last);

        double best = double.MaxValue;
        for (int s = FirstSegmentAtOrAfter(first); s < _segments.Count && _segments[s].LineIndex <= last; s++)
        {
            var segment = _segments[s];
            double d = Distance(segment, work.X, work.Y);
            // Strictly nearer: on a corner shared by two lines, the earlier one, which is still finishing.
            if (d < best - 1e-6)
            {
                best = d;
                _executing = segment.LineIndex;
                kind = segment.Kind;
            }
        }
        return _executing;
    }

    int FirstSegmentAtOrAfter(int lineIndex)
    {
        int lo = 0, hi = _segments.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_segments[mid].LineIndex < lineIndex)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    static double Distance(ToolpathSegment s, double x, double y)
    {
        double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((x - s.X1) * dx + (y - s.Y1) * dy) / lengthSquared, 0, 1);
        double px = s.X1 + t * dx - x, py = s.Y1 + t * dy - y;
        return Math.Sqrt(px * px + py * py);
    }
}
