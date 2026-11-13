using System.Globalization;

namespace GrblCore.Drawing;

/// <summary>
/// G-code that burns a <see cref="LaserDrawing"/>: for each path a rapid move to its start, the laser on (M3),
/// G1 along it, the laser off (M5); at the end back to X0 Y0. Coordinates are in mm, absolute, from the work zero; the laser power (S) for
/// Grbl 0.9+ is sent by the program before the job, as for any file. No Z and no S anywhere: a file with them is taken for
/// an ArtCAM file (<see cref="ArtCamLaserConverter.NeedsConversion"/>), so opening it again would offer a conversion.
/// </summary>
public static class LaserGCode
{
    /// <param name="feed">Burning speed, mm/min.</param>
    /// <param name="passes">How many times the whole drawing is burned (deeper or darker).</param>
    public static List<string> FromDrawing(LaserDrawing drawing, int feed, int passes = 1)
    {
        if (feed <= 0)
            throw new ArgumentOutOfRangeException(nameof(feed), "The feed must be positive.");
        if (passes <= 0)
            throw new ArgumentOutOfRangeException(nameof(passes), "At least one pass is required.");

        var lines = new List<string> { "G21", "G90", "M5", "F" + feed.ToString(CultureInfo.InvariantCulture) };
        string? at = null; // where the head is, as written (rounded), while the laser is on
        for (int pass = 0; pass < passes; pass++)
        {
            foreach (var path in drawing.Paths)
            {
                // Rounded to what is written, and without repeats: a path that ends up a single point burns nothing.
                var points = new List<string>();
                foreach (var p in path)
                {
                    var xy = XY(p);
                    if (points.Count == 0 || points[^1] != xy)
                        points.Add(xy);
                }
                if (points.Count < 2)
                    continue;
                // A path that starts where the last one ended goes on without switching the laser off and on.
                if (at != points[0])
                {
                    if (at is not null)
                        lines.Add("M5");
                    lines.Add("G0 " + points[0]);
                    lines.Add("M3");
                }
                lines.AddRange(points.Skip(1).Select(xy => "G1 " + xy));
                at = points[^1];
            }
        }
        if (at is not null)
            lines.Add("M5");
        // Back to the work zero with the laser off. Z is never moved, so it is still where the job started.
        lines.Add("G0 X0 Y0");
        return lines;
    }

    static string XY(PathPoint p) => $"X{Number(p.X)} Y{Number(p.Y)}";

    static string Number(double value)
    {
        var text = Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
}
