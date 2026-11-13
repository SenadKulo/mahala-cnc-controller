namespace GrblCore.Drawing;

/// <summary>A point of a drawing in mm, Y up, as on the machine.</summary>
public readonly record struct PathPoint(double X, double Y);

/// <summary>
/// Lines to burn: each path is burned in one go from its first point to its last, the laser off between paths.
/// <see cref="Width"/> and <see cref="Height"/> are the size of what is burned (mm); its bottom-left corner is at 0,0.
/// </summary>
public sealed record LaserDrawing(IReadOnlyList<IReadOnlyList<PathPoint>> Paths, double Width, double Height)
{
    /// <summary>Moves the paths so the bottom-left corner of everything is at 0,0, and measures them.</summary>
    public static LaserDrawing FromPaths(IReadOnlyList<IReadOnlyList<PathPoint>> paths)
    {
        var points = paths.SelectMany(p => p).ToList();
        if (points.Count == 0)
            return new LaserDrawing([], 0, 0);
        double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
        double maxX = points.Max(p => p.X), maxY = points.Max(p => p.Y);
        return new LaserDrawing(
            [.. paths.Select(path => (IReadOnlyList<PathPoint>)[.. path.Select(p => new PathPoint(p.X - minX, p.Y - minY))])],
            maxX - minX, maxY - minY);
    }

    /// <summary>The same drawing <paramref name="factor"/> times bigger (from its bottom-left corner, which stays at 0,0).</summary>
    public LaserDrawing Scaled(double factor)
    {
        if (!(factor > 0) || !double.IsFinite(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), "The scale must be positive.");
        return new LaserDrawing(
            [.. Paths.Select(path => (IReadOnlyList<PathPoint>)[.. path.Select(p => new PathPoint(p.X * factor, p.Y * factor))])],
            Width * factor, Height * factor);
    }
}
