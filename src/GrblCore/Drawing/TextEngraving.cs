using System.Globalization;

namespace GrblCore.Drawing;

/// <summary>
/// Text as single-stroke lines for engraving (<see cref="HersheySimplex"/>, with Bosnian č ć š ž đ added):
/// left-aligned, line after line, the bottom-left corner of the whole text at 0,0.
/// </summary>
public static class TextEngraving
{
    /// <summary>From one line's baseline to the next, in font units (capitals are 21): room for accents and descenders.</summary>
    const double LineUnits = 36;

    sealed record Glyph(double Advance, IReadOnlyList<IReadOnlyList<PathPoint>> Strokes);

    static readonly Dictionary<char, Glyph> Glyphs = Build();

    /// <summary>The characters of <paramref name="text"/> the font doesn't have, each once ("" when all are there).</summary>
    public static string Unsupported(string text) =>
        new([.. text.Where(c => c is not ('\n' or '\r' or '\t') && !Glyphs.ContainsKey(c)).Distinct()]);

    /// <param name="capHeight">Height of a capital letter, mm.</param>
    /// <exception cref="ArgumentException">The text has characters the font doesn't have (<see cref="Unsupported"/>).</exception>
    public static LaserDrawing Layout(string text, double capHeight)
    {
        if (Unsupported(text) is { Length: > 0 } missing)
            throw new ArgumentException($"Characters not in the font: {missing}", nameof(text));
        if (!(capHeight > 0))
            throw new ArgumentOutOfRangeException(nameof(capHeight), "The height must be positive.");

        double scale = capHeight / HersheySimplex.CapHeight;
        var paths = new List<IReadOnlyList<PathPoint>>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Split('\n');
        for (int row = 0; row < lines.Length; row++)
        {
            double x = 0, y = -row * LineUnits;
            foreach (var c in lines[row])
            {
                var glyph = Glyphs[c];
                foreach (var stroke in glyph.Strokes)
                    paths.Add([.. stroke.Select(p => new PathPoint((x + p.X) * scale, (y + p.Y) * scale))]);
                x += glyph.Advance;
            }
        }
        return LaserDrawing.FromPaths(paths);
    }

    static Dictionary<char, Glyph> Build()
    {
        var glyphs = new Dictionary<char, Glyph>();
        for (int i = 0; i < HersheySimplex.Glyphs.Length; i++)
            glyphs[(char)(32 + i)] = Parse(HersheySimplex.Glyphs[i]);

        // Bosnian letters: the base letter with a caron, an acute or a bar, in the font's own units.
        const int upper = HersheySimplex.CapHeight, lower = HersheySimplex.XHeight;
        PathPoint[] Caron(double cx, int top) => [new(cx - 3, top + 5), new(cx, top + 2), new(cx + 3, top + 5)];
        PathPoint[] Acute(double cx, int top) => [new(cx - 1, top + 2), new(cx + 3, top + 6)];
        void Add(char letter, char baseLetter, PathPoint[] mark)
        {
            var b = glyphs[baseLetter];
            glyphs[letter] = new Glyph(b.Advance, [.. b.Strokes, mark]);
        }
        foreach (var (letter, baseLetter) in new[] { ('Č', 'C'), ('Š', 'S'), ('Ž', 'Z') })
            Add(letter, baseLetter, Caron(Center(glyphs[baseLetter]), upper));
        foreach (var (letter, baseLetter) in new[] { ('č', 'c'), ('š', 's'), ('ž', 'z') })
            Add(letter, baseLetter, Caron(Center(glyphs[baseLetter]), lower));
        Add('Ć', 'C', Acute(Center(glyphs['C']), upper));
        Add('ć', 'c', Acute(Center(glyphs['c']), lower));
        // The bar crosses the stem: D's is at x 4 (half its height), d's at x 15 (near its top).
        Add('Đ', 'D', [new(1, 11), new(9, 11)]);
        Add('đ', 'd', [new(12, 18), new(18, 18)]);
        return glyphs;
    }

    static double Center(Glyph glyph)
    {
        var xs = glyph.Strokes.SelectMany(s => s).Select(p => p.X).ToList();
        return (xs.Min() + xs.Max()) / 2;
    }

    /// <summary>"advance;x y x y|x y ..." (<see cref="HersheySimplex.Glyphs"/>).</summary>
    static Glyph Parse(string entry)
    {
        var parts = entry.Split(';');
        var strokes = new List<IReadOnlyList<PathPoint>>();
        foreach (var stroke in parts[1].Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var numbers = stroke.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray();
            strokes.Add([.. Enumerable.Range(0, numbers.Length / 2).Select(i => new PathPoint(numbers[2 * i], numbers[2 * i + 1]))]);
        }
        return new Glyph(double.Parse(parts[0], CultureInfo.InvariantCulture), strokes);
    }
}
