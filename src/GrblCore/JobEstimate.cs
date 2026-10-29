namespace GrblCore;

/// <summary>Grbl settings that decide how fast a program runs.</summary>
/// <param name="SeekRate">G0 speed, mm/min ($5 in Grbl 0.8).</param>
/// <param name="DefaultFeed">Feed when the program has no F, mm/min ($4).</param>
/// <param name="Acceleration">mm/s² ($8).</param>
/// <param name="JunctionDeviation">mm ($9) - how fast Grbl takes corners.</param>
public sealed record MachineLimits(double SeekRate, double DefaultFeed, double Acceleration, double JunctionDeviation);

/// <summary>
/// How long a program takes, from a simulation of Grbl 0.8's planner: trapezoid speed profiles,
/// corner speeds from the junction deviation, a stop whenever the laser switches (M3/M5 wait
/// for the moves before them), and the serial line as a floor for many tiny moves.
/// </summary>
public sealed class JobEstimate
{
    /// <summary>Seconds from the start until line i (index into the program) is done.</summary>
    readonly double[] _secondsAfterLine;

    public TimeSpan Total { get; }

    JobEstimate(double[] secondsAfterLine)
    {
        _secondsAfterLine = secondsAfterLine;
        Total = TimeSpan.FromSeconds(secondsAfterLine.Length > 0 ? secondsAfterLine[^1] : 0);
    }

    /// <summary>Time left once Grbl has accepted <paramref name="completedLines"/> lines.</summary>
    public TimeSpan Remaining(int completedLines)
    {
        if (completedLines <= 0)
            return Total;
        if (completedLines >= _secondsAfterLine.Length)
            return TimeSpan.Zero;
        return Total - TimeSpan.FromSeconds(_secondsAfterLine[completedLines - 1]);
    }

    public static JobEstimate Calculate(GCodeProgram program, Toolpath toolpath, MachineLimits limits, int baudRate)
    {
        var lineSeconds = new double[program.Lines.Count];
        foreach (var (lineIndex, seconds) in MoveTimes(toolpath.Segments.Where(s => s.Length > 1e-6).ToList(), limits))
            lineSeconds[lineIndex] += seconds;

        // Each line has to arrive over the serial port first (10 bits per character, plus the newline).
        // Short engraving moves can finish faster than the next line arrives.
        double bytesPerSecond = baudRate / 10.0;
        var cumulative = new double[lineSeconds.Length];
        double total = 0;
        for (int i = 0; i < lineSeconds.Length; i++)
        {
            total += Math.Max(lineSeconds[i], (program.Lines[i].Text.Length + 1) / bytesPerSecond);
            cumulative[i] = total;
        }
        return new JobEstimate(cumulative);
    }

    static IEnumerable<(int LineIndex, double Seconds)> MoveTimes(List<ToolpathSegment> moves, MachineLimits limits)
    {
        int n = moves.Count;
        double accel = limits.Acceleration;
        var speed = new double[n];  // nominal, mm/s
        var length = new double[n];
        var entry = new double[n];  // speed at the start of each move, mm/s

        for (int k = 0; k < n; k++)
        {
            var m = moves[k];
            length[k] = m.Length;
            double feed = m.Kind == SegmentKind.Rapid ? limits.SeekRate : m.Feed > 0 ? m.Feed : limits.DefaultFeed;
            speed[k] = feed / 60;
            entry[k] = k == 0 ? 0 : JunctionSpeed(moves[k - 1], speed[k - 1], m, speed[k], limits);
        }

        // Backward pass: every move must be able to slow down to what the next one allows (0 at the end).
        double exit = 0;
        for (int k = n - 1; k >= 0; k--)
        {
            entry[k] = Math.Min(entry[k], Math.Sqrt(exit * exit + 2 * accel * length[k]));
            exit = entry[k];
        }
        // Forward pass: and must be reachable by speeding up from the previous one.
        for (int k = 1; k < n; k++)
            entry[k] = Math.Min(entry[k], Math.Sqrt(entry[k - 1] * entry[k - 1] + 2 * accel * length[k - 1]));

        for (int k = 0; k < n; k++)
            yield return (moves[k].LineIndex, TrapezoidSeconds(length[k], entry[k], k + 1 < n ? entry[k + 1] : 0, speed[k], accel));
    }

    /// <summary>Highest speed for passing from one move into the next, as Grbl 0.8's planner computes it.</summary>
    static double JunctionSpeed(ToolpathSegment previous, double previousSpeed, ToolpathSegment current, double currentSpeed, MachineLimits limits)
    {
        // M3/M5 between the moves: Grbl finishes the first move completely before switching.
        if ((previous.Kind == SegmentKind.Cut) != (current.Kind == SegmentKind.Cut))
            return 0;

        double pl = previous.Length, cl = current.Length;
        double cosTheta = -((previous.X2 - previous.X1) * (current.X2 - current.X1) + (previous.Y2 - previous.Y1) * (current.Y2 - current.Y1)) / (pl * cl);
        if (cosTheta >= 0.95)
            return 0; // turning back on itself
        double v = Math.Min(previousSpeed, currentSpeed);
        if (cosTheta > -0.95)
        {
            double sinHalf = Math.Sqrt(0.5 * (1 - cosTheta));
            v = Math.Min(v, Math.Sqrt(limits.Acceleration * limits.JunctionDeviation * sinHalf / (1 - sinHalf)));
        }
        return v;
    }

    static double TrapezoidSeconds(double length, double entry, double exit, double cruise, double accel)
    {
        double accelDistance = (cruise * cruise - entry * entry) / (2 * accel);
        double decelDistance = (cruise * cruise - exit * exit) / (2 * accel);
        if (accelDistance + decelDistance <= length)
            return (cruise - entry) / accel + (cruise - exit) / accel + (length - accelDistance - decelDistance) / cruise;
        // Too short to reach full speed: accelerate to a peak, then brake.
        double peak = Math.Sqrt((2 * accel * length + entry * entry + exit * exit) / 2);
        return (peak - entry) / accel + (peak - exit) / accel;
    }
}
