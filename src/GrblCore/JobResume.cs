using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GrblCore;

public enum ResumeProblemKind
{
    /// <summary>No move left from the chosen line to the end of the program.</summary>
    NothingLeft,
    /// <summary>
    /// A line before the start changes the coordinate system or moves in machine coordinates
    /// (G92, G10, G28, G30, G53, G55-G59), so the start point can't be worked out.
    /// </summary>
    UnsupportedCommand,
    /// <summary>The first line with its motion mode (G1/G2/G3) added doesn't fit Grbl's line buffer.</summary>
    LineTooLong,
}

/// <param name="Line">The line that causes the problem, if one does.</param>
public sealed record ResumeProblem(ResumeProblemKind Kind, GCodeLine? Line = null);

/// <summary>How to continue a program in the middle.</summary>
/// <param name="StartIndex">Index into <see cref="GCodeProgram.Lines"/> of the first line sent.</param>
/// <param name="Start">Work position the laser is brought to before that line (where the previous line ended).</param>
/// <param name="Preamble">Sent first: laser off, move to <paramref name="Start"/>, then the modes, feed and laser state in effect there.</param>
/// <param name="FirstLine">Text sent instead of the start line, with the motion mode added if the line relies on an earlier one.</param>
public sealed record ResumePlan(int StartIndex, Position Start, IReadOnlyList<string> Preamble, string FirstLine);

/// <summary>Continues a job from a chosen line, e.g. after Grbl was reset or the connection dropped.</summary>
public static class JobResume
{
    /// <summary>
    /// Grbl 0.8 answers "ok" when a line enters its planner buffer (18 moves), not when it has run.
    /// Lines answered last may not have run yet, so the start is searched this far back.
    /// </summary>
    public const int SearchLines = 40;

    /// <summary>Status positions have 3 decimals and arcs are drawn as straight pieces 0.01 mm off.</summary>
    const double OnPathTolerance = 0.05;

    static readonly HashSet<decimal> UnsupportedG = [10, 28, 28.1m, 30, 30.1m, 53, 55, 56, 57, 58, 59, 92, 92.1m];

    /// <summary>
    /// Line to continue from: the earliest line, among the last ones Grbl accepted, whose path passes
    /// through the last reported position. Burning a bit twice is better than leaving a gap.
    /// </summary>
    /// <param name="lastCompletedIndex">Index of the last line Grbl answered.</param>
    /// <param name="stoppedAt">Last work position reported while the job ran, if known.</param>
    public static int SuggestStart(Toolpath toolpath, int lastCompletedIndex, Position? stoppedAt)
    {
        int first = Math.Max(0, lastCompletedIndex - SearchLines);
        if (stoppedAt is { } p)
        {
            foreach (var s in toolpath.Segments)
            {
                if (s.LineIndex > lastCompletedIndex)
                    break;
                if (s.LineIndex >= first && Distance(p, s) <= OnPathTolerance)
                    return s.LineIndex;
            }
        }
        return first;
    }

    /// <summary>
    /// Works out how to continue <paramref name="program"/> from line <paramref name="fromIndex"/>
    /// (or the first move after it), with the machine now at <paramref name="currentWork"/>.
    /// </summary>
    public static bool TryPlan(GCodeProgram program, int fromIndex, Position currentWork,
        [NotNullWhen(true)] out ResumePlan? plan, [NotNullWhen(false)] out ResumeProblem? problem)
    {
        plan = null;
        // Lines without a move (F, M3, G90...) only change modes; they are replayed by the preamble.
        int start = Math.Max(0, fromIndex);
        while (start < program.Lines.Count && !HasAxisWords(GCodeWord.Parse(program.Lines[start].Text)))
            start++;
        if (start >= program.Lines.Count)
        {
            problem = new ResumeProblem(ResumeProblemKind.NothingLeft);
            return false;
        }

        var state = new ModalState();
        for (int i = 0; i < start; i++)
        {
            if (!state.Apply(GCodeWord.Parse(program.Lines[i].Text)))
            {
                problem = new ResumeProblem(ResumeProblemKind.UnsupportedCommand, program.Lines[i]);
                return false;
            }
        }

        var line = program.Lines[start];
        var firstLine = line.Text;
        var words = GCodeWord.Parse(firstLine);
        // The preamble ends with G0, so a line continuing an earlier G1/G2/G3 needs that word back,
        // written in the line's own style ("X1 Y2" or "X1Y2").
        if (!words.Any(w => w.IsMotion(out _)) && state.Motion is "G1" or "G2" or "G3")
            firstLine = state.Motion + (words.Count > 1 && !firstLine.Contains(' ') ? "" : " ") + firstLine;
        if (firstLine.Count(c => !char.IsWhiteSpace(c)) > GCodeProgram.DefaultMaxLineLength)
        {
            problem = new ResumeProblem(ResumeProblemKind.LineTooLong, line);
            return false;
        }
        if (firstLine.Length > GrblConnection.MaxLineLength)
            firstLine = firstLine.Replace(" ", "");

        var target = new Position(state.X, state.Y, state.ZUsed ? state.Z : currentWork.Z);
        var preamble = new List<string> { "M5", "G21 G90" };
        // Same order as going to zero: up before travelling, down after it.
        bool zUp = target.Z > currentWork.Z + PositionRecovery.Tolerance;
        bool zDown = target.Z < currentWork.Z - PositionRecovery.Tolerance;
        if (zUp)
            preamble.Add($"G0 Z{N(target.Z)}");
        preamble.Add($"G0 X{N(target.X)} Y{N(target.Y)}");
        if (zDown)
            preamble.Add($"G0 Z{N(target.Z)}");
        preamble.Add($"{state.Plane} {(state.Inches ? "G20" : "G21")} {(state.Absolute ? "G90" : "G91")} {(state.InverseTime ? "G93" : "G94")}"
            + (state.Feed is { } feed ? $" F{feed}" : ""));
        if (state.Laser is { } laser)
            preamble.Add(laser + (state.Power is { } power ? " S" + power : ""));

        plan = new ResumePlan(start, target, preamble, firstLine);
        problem = null;
        return true;
    }

    static bool HasAxisWords(List<GCodeWord> words) => words.Any(w => w.Letter is 'X' or 'Y' or 'Z' or 'I' or 'J' or 'K' or 'R');

    static double Distance(Position p, ToolpathSegment s)
    {
        double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - s.X1) * dx + (p.Y - s.Y1) * dy) / lengthSquared, 0, 1);
        double ex = s.X1 + t * dx - p.X, ey = s.Y1 + t * dy - p.Y;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    static string N(double value) => (Math.Round(value, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Modes and position after running lines, as Grbl would have them. Starts at work 0,0,0 like <see cref="Toolpath"/>.</summary>
    sealed class ModalState
    {
        public string Motion = "G0";
        public string Plane = "G17";
        public bool Absolute = true, Inches, InverseTime, ZUsed;
        /// <summary>F as written in the program, in its own units.</summary>
        public string? Feed;
        /// <summary>"M3"/"M4" while the laser is on.</summary>
        public string? Laser;
        /// <summary>S as written in the program (laser power on 0.9+), if it sets one.</summary>
        public string? Power;
        /// <summary>Work position in mm.</summary>
        public double X, Y, Z;

        /// <summary>False if the line uses a command whose effect can't be replayed.</summary>
        public bool Apply(List<GCodeWord> words)
        {
            double? x = null, y = null, z = null;
            bool dwell = false;
            foreach (var w in words)
            {
                w.TryGetNumber(out var number);
                switch (w.Letter)
                {
                    case 'G' when UnsupportedG.Contains(number): return false;
                    case 'G' when w.IsMotion(out var m): Motion = m; break;
                    case 'G' when number == 80: Motion = "G80"; break;
                    case 'G' when number == 4: dwell = true; break;
                    case 'G' when number is 17 or 18 or 19: Plane = "G" + (int)number; break;
                    case 'G' when number == 20: Inches = true; break;
                    case 'G' when number == 21: Inches = false; break;
                    case 'G' when number == 90: Absolute = true; break;
                    case 'G' when number == 91: Absolute = false; break;
                    case 'G' when number == 93: InverseTime = true; break;
                    case 'G' when number == 94: InverseTime = false; break;
                    case 'M' when number is 3 or 4: Laser = "M" + (int)number; break;
                    case 'M' when number == 5: Laser = null; break;
                    case 'F': Feed = w.Value; break;
                    case 'S': Power = w.Value; break;
                    case 'X': x = (double)number; break;
                    case 'Y': y = (double)number; break;
                    case 'Z': z = (double)number; break;
                }
            }
            if (dwell || Motion == "G80")
                return true;
            // Units and distance mode of this line apply to its coordinates, wherever they are written.
            double scale = Inches ? 25.4 : 1;
            X = Move(X, x, scale);
            Y = Move(Y, y, scale);
            Z = Move(Z, z, scale);
            ZUsed |= z is not null;
            return true;
        }

        double Move(double current, double? value, double scale) =>
            value is not { } v ? current : Absolute ? v * scale : current + v * scale;
    }
}
