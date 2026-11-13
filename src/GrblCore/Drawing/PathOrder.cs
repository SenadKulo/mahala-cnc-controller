namespace GrblCore.Drawing;

/// <summary>The order the laser burns paths in, so it doesn't zigzag across the sheet between them.</summary>
public static class PathOrder
{
    /// <summary>Ordering compares each path with each; above this many they keep their order.</summary>
    public const int MaxPathsToOrder = 5000;

    /// <summary>
    /// Nearest next path from where the laser is, starting at the bottom-left; an open path may be burned from
    /// either end. Drawing programs and fonts give paths in their own order, which can zigzag across the whole sheet.
    /// </summary>
    public static List<IReadOnlyList<PathPoint>> NearestNext(IEnumerable<IReadOnlyList<PathPoint>> paths)
    {
        var left = paths.Where(p => p.Count > 0).Select(p => p.ToList()).ToList();
        if (left.Count == 0 || left.Count > MaxPathsToOrder)
            return [.. left];
        double minX = left.SelectMany(p => p).Min(p => p.X), minY = left.SelectMany(p => p).Min(p => p.Y);
        var at = new PathPoint(minX, minY);
        var ordered = new List<IReadOnlyList<PathPoint>>(left.Count);
        while (left.Count > 0)
        {
            int best = 0;
            bool reverse = false;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < left.Count; i++)
            {
                var path = left[i];
                double toStart = Distance2(at, path[0]);
                if (toStart < bestDistance)
                    (best, reverse, bestDistance) = (i, false, toStart);
                if (path[0] != path[^1])
                {
                    double toEnd = Distance2(at, path[^1]);
                    if (toEnd < bestDistance)
                        (best, reverse, bestDistance) = (i, true, toEnd);
                }
            }
            var next = left[best];
            left.RemoveAt(best);
            if (reverse)
                next.Reverse();
            ordered.Add(next);
            at = next[^1];
        }
        return ordered;
    }

    static double Distance2(PathPoint a, PathPoint b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
