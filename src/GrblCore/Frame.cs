using System.Globalization;

namespace GrblCore;

/// <summary>Traces the outline of a job with the laser off, to check where it lands on the material.</summary>
public static class Frame
{
    /// <summary>
    /// Laser off, rapid around <paramref name="bounds"/> starting at its lower left corner,
    /// then back to <paramref name="returnTo"/>. Z is left alone.
    /// </summary>
    public static IReadOnlyList<string> Commands(Bounds bounds, Position returnTo) =>
    [
        "M5",
        $"G90 G0 X{N(bounds.MinX)} Y{N(bounds.MinY)}",
        $"G0 X{N(bounds.MaxX)}",
        $"G0 Y{N(bounds.MaxY)}",
        $"G0 X{N(bounds.MinX)}",
        $"G0 Y{N(bounds.MinY)}",
        $"G0 X{N(returnTo.X)} Y{N(returnTo.Y)}",
    ];

    static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
