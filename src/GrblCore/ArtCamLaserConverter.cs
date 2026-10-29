using System.Globalization;
using System.Text.RegularExpressions;

namespace GrblCore;

/// <summary>A toolpath in an ArtCAM file. ArtCAM starts each one with "M3 S15000" (M3 with a speed).</summary>
/// <param name="Tool">ArtCAM's tool comment, e.g. "Tool Number:1 0.300 mm dia. slot drill".</param>
/// <param name="Contours">How many times the tool goes down into the material.</param>
/// <param name="Passes">Distinct cutting depths - on a laser each one is another pass over the same contour.</param>
public sealed record ArtCamToolpath(int Number, string? Tool, int Contours, int Passes);

/// <summary>
/// Turns an ArtCAM router file into a laser file. Works on parsed words, not text patterns, so any
/// safe height, depth, spacing or feed value is handled:
/// going down to Z ≤ 0 switches the laser on (M3), going up switches it off (M5),
/// Z, S and ArtCAM's F words are removed and each toolpath gets the feed rate chosen by the user.
/// </summary>
public static partial class ArtCamLaserConverter
{
    /// <summary>True for an original ArtCAM file (Z moves or spindle speed); false for one already converted.</summary>
    public static bool NeedsConversion(IEnumerable<string> lines) =>
        lines.Any(l => GCodeWord.Parse(GCodeProgram.Clean(l)).Any(w => w.Letter is 'Z' or 'S'));

    public static IReadOnlyList<ArtCamToolpath> FindToolpaths(IEnumerable<string> lines)
    {
        var lineList = lines as IReadOnlyList<string> ?? lines.ToList();
        var result = new List<ArtCamToolpath>();
        string? lastToolComment = null, tool = null;
        int contours = 0;
        var depths = new HashSet<decimal>();
        bool inToolpath = MovesBeforeFirstToolpath(lineList), down = false;

        void Finish()
        {
            if (inToolpath || contours > 0)
                result.Add(new ArtCamToolpath(result.Count + 1, tool, contours, depths.Count));
        }

        foreach (var line in lineList)
        {
            if (ToolComment(line) is { } comment)
            {
                lastToolComment = comment;
                tool ??= comment; // tool of a section that starts without "M3 S"
            }

            var words = GCodeWord.Parse(GCodeProgram.Clean(line));
            if (IsToolpathStart(words))
            {
                Finish();
                inToolpath = true;
                tool = lastToolComment;
                contours = 0;
                depths.Clear();
            }
            else if (words.Any(IsLaserOn))
            {
                // Plain M3 in a partly converted file: the laser goes on like a plunge would.
                if (!down)
                    contours++;
                down = true;
            }
            else if (words.Any(w => w.Is('M', 5)))
            {
                down = false;
            }
            if (TryGetZ(words, out var z))
            {
                if (z <= 0)
                {
                    if (!down)
                        contours++;
                    down = true;
                    depths.Add(z);
                }
                else
                {
                    down = false;
                }
            }
        }
        Finish();
        return result;
    }

    /// <param name="feedRates">Feed per toolpath (mm/min). Toolpaths beyond the list use the last value.</param>
    public static List<string> Convert(IReadOnlyList<string> lines, IReadOnlyList<int> feedRates)
    {
        if (feedRates.Count == 0)
            throw new ArgumentException("At least one feed rate is required.", nameof(feedRates));

        // Moves before the first "M3 S" (or no "M3 S" at all) form a toolpath of their own.
        bool leadingToolpath = MovesBeforeFirstToolpath(lines);
        var output = new List<string>();
        bool started = false, laserOn = false;
        int toolpath = -1;
        // Motion mode (G0-G3) as the source file has it, and as our output has it. Lines that end up
        // with only "G1" are dropped, so the mode is carried over to the next line that moves.
        string? sourceMotion = null, outputMotion = null;

        string Feed() => "F" + feedRates[Math.Min(toolpath, feedRates.Count - 1)].ToString(CultureInfo.InvariantCulture);

        foreach (var line in lines)
        {
            var words = GCodeWord.Parse(GCodeProgram.Clean(line));
            if (words.Count == 0)
            {
                output.Add(line.TrimEnd()); // comments and blank lines stay as they are
                continue;
            }
            if (!started)
            {
                started = true;
                output.Add("M5"); // laser off until the first cut, whatever state it was left in
                if (leadingToolpath)
                {
                    toolpath = 0;
                    output.Add(Feed());
                }
            }

            var kept = new List<GCodeWord>();
            decimal? z = null;
            bool changed = false, hasMotion = false;
            bool toolpathStart = IsToolpathStart(words);
            foreach (var w in words)
            {
                if (IsLaserOn(w) && toolpathStart)
                {
                    // Start of a toolpath. The laser itself is switched on when the tool goes down.
                    toolpath++;
                    output.Add(Feed());
                    changed = true;
                }
                else if (IsLaserOn(w))
                {
                    // Plain M3 without speed: the file already switches the laser itself (partly converted by hand).
                    if (!laserOn)
                        output.Add("M3");
                    laserOn = true;
                    changed = true;
                }
                else if (w.Is('M', 5))
                {
                    if (laserOn)
                        output.Add("M5");
                    laserOn = false;
                    changed = true;
                }
                else if (w.Letter is 'S' or 'F')
                {
                    changed = true;
                }
                else if (w.Letter == 'Z')
                {
                    if (w.TryGetNumber(out var value))
                        z = value;
                    changed = true;
                }
                else
                {
                    if (w.IsMotion(out var motion))
                    {
                        sourceMotion = motion;
                        hasMotion = true;
                    }
                    kept.Add(w);
                }
            }

            if (z is { } depth)
            {
                if (depth <= 0 && !laserOn)
                {
                    output.Add("M3");
                    laserOn = true;
                }
                else if (depth > 0 && laserOn)
                {
                    output.Add("M5");
                    laserOn = false;
                }
            }

            bool moves = kept.Any(w => w.Letter is 'X' or 'Y' or 'I' or 'J' or 'K' or 'R');
            bool needsMotion = moves && !hasMotion && sourceMotion is not null && sourceMotion != outputMotion;

            if (!changed && !needsMotion)
            {
                output.Add(line.TrimEnd());
            }
            else if (moves || kept.Any(w => !w.IsMotion(out _)))
            {
                var text = string.Join(" ", kept.Select(w => w.Text));
                output.Add(needsMotion ? sourceMotion + " " + text : text);
            }
            else
            {
                continue; // only a motion word was left, e.g. "G1" from "G1 Z-1 F6"
            }

            if (hasMotion || needsMotion)
                outputMotion = sourceMotion;
        }

        if (laserOn)
            output.Add("M5");
        return output;
    }

    static bool IsLaserOn(GCodeWord w) => w.Is('M', 3) || w.Is('M', 4);

    /// <summary>ArtCAM starts every toolpath with "M3 S15000". A plain "M3" is a laser switch, not a toolpath.</summary>
    static bool IsToolpathStart(List<GCodeWord> words) => words.Any(IsLaserOn) && words.Any(w => w.Letter == 'S');

    static bool MovesBeforeFirstToolpath(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var words = GCodeWord.Parse(GCodeProgram.Clean(line));
            if (IsToolpathStart(words))
                return false;
            // Only X/Y: ArtCAM goes to safe height ("G0Z5.0000") before its first "M3 S15000".
            if (words.Any(w => w.Letter is 'X' or 'Y'))
                return true;
        }
        return true; // no "M3 S" at all - the whole file is one toolpath
    }

    static bool TryGetZ(List<GCodeWord> words, out decimal z)
    {
        foreach (var w in words)
        {
            if (w.Letter == 'Z' && w.TryGetNumber(out z))
                return true;
        }
        z = 0;
        return false;
    }

    static string? ToolComment(string line)
    {
        if (!line.Contains("(Tool", StringComparison.OrdinalIgnoreCase))
            return null;
        return Whitespace().Replace(line.Replace('(', ' ').Replace(')', ' '), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
