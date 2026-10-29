using System.Globalization;

namespace GrblCore;

public enum Axis { X, Y, Z }

/// <summary>Manual axis moves. Grbl 0.8c has no $J jogging, so a jog is a relative rapid move.</summary>
public static class Jog
{
    /// <summary>Step sizes offered in the UI, mm.</summary>
    public static readonly IReadOnlyList<double> Steps = [0.1, 1, 10, 100];

    /// <summary>
    /// Lines that move one axis by <paramref name="distance"/> mm (negative = minus direction),
    /// then switch back to absolute mode so later lines aren't read as relative.
    /// </summary>
    public static IReadOnlyList<string> Commands(Axis axis, double distance)
    {
        if (distance == 0 || !double.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "Jog distance must be a non-zero number.");
        return [$"G91 G21 G0 {axis}{distance.ToString("0.###", CultureInfo.InvariantCulture)}", "G90"];
    }
}
