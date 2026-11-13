using QRCoder;

namespace GrblCore.Drawing;

/// <summary>A QR code to burn: its lines, how many modules (squares) a side has, and a module's size in mm.</summary>
public sealed record QrLayout(LaserDrawing Drawing, int Modules, double ModuleSize);

/// <summary>
/// A QR code (error correction M, about 15 % may be damaged) as filled squares: each row of dark modules is burned
/// as horizontal lines a beam's width apart, left to right and back again in turn. The code's bottom-left corner is at
/// 0,0; the empty margin a scanner needs (4 modules) is not part of the size and must be left free on the material.
/// </summary>
public static class QrEngraving
{
    const int QuietZone = 4;

    /// <param name="size">Side of the code, mm (without the margin).</param>
    /// <param name="lineSpacing">Distance between burned lines, mm: about the laser's spot. Rounded so each module gets whole lines.</param>
    /// <exception cref="ArgumentException">Empty text, or too long for a QR code.</exception>
    public static QrLayout Layout(string text, double size, double lineSpacing)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("The text is empty.", nameof(text));
        if (!(size > 0) || !double.IsFinite(size))
            throw new ArgumentOutOfRangeException(nameof(size), "The size must be positive.");
        if (!(lineSpacing > 0) || !double.IsFinite(lineSpacing))
            throw new ArgumentOutOfRangeException(nameof(lineSpacing), "The line spacing must be positive.");

        List<System.Collections.BitArray> matrix;
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            matrix = data.ModuleMatrix;
        }
        catch (QRCoder.Exceptions.DataTooLongException ex)
        {
            throw new ArgumentException("The text is too long for a QR code.", nameof(text), ex);
        }

        int modules = matrix.Count - 2 * QuietZone;
        double module = size / modules;
        int linesPerModule = Math.Max(1, (int)Math.Round(module / lineSpacing));
        double spacing = module / linesPerModule;
        // The spot is about as wide as the spacing: lines stop half a spot short of the module's ends, as they
        // start half a spacing inside its top and bottom. Never more than a quarter module, so a lone module still burns.
        double inset = Math.Min(Math.Min(lineSpacing, spacing) / 2, module / 4);

        var paths = new List<IReadOnlyList<PathPoint>>();
        bool leftToRight = true;
        for (int row = 0; row < modules; row++)
        {
            var dark = matrix[row + QuietZone];
            // Runs of dark modules in this row: [first, last].
            var runs = new List<(int First, int Last)>();
            for (int column = 0; column < modules; column++)
            {
                if (!dark[column + QuietZone])
                    continue;
                if (runs.Count > 0 && runs[^1].Last == column - 1)
                    runs[^1] = (runs[^1].First, column);
                else
                    runs.Add((column, column));
            }
            for (int line = 0; line < linesPerModule; line++)
            {
                // Row 0 is the top: Y goes up on the machine.
                double y = size - row * module - (line + 0.5) * spacing;
                var segments = runs.Select(r => (From: r.First * module + inset, To: (r.Last + 1) * module - inset));
                foreach (var (from, to) in leftToRight ? segments : segments.Reverse())
                    paths.Add(leftToRight ? [new PathPoint(from, y), new PathPoint(to, y)] : [new PathPoint(to, y), new PathPoint(from, y)]);
                if (runs.Count > 0)
                    leftToRight = !leftToRight;
            }
        }
        return new QrLayout(new LaserDrawing(paths, size, size), modules, module);
    }
}
