using System.Globalization;
using System.Text.RegularExpressions;

namespace GrblCore;

/// <summary>One G-code word, e.g. "G1", "X10.5", "M3".</summary>
public readonly partial record struct GCodeWord(char Letter, string Value)
{
    public string Text => Letter + Value;

    public bool TryGetNumber(out decimal number) =>
        decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    public bool Is(char letter, decimal number) => Letter == letter && TryGetNumber(out var n) && n == number;

    /// <summary>G0-G3. Normalized to e.g. "G1" (ArtCAM writes "G01" in some files).</summary>
    public bool IsMotion(out string normalized)
    {
        if (Letter == 'G' && TryGetNumber(out var n) && n is 0 or 1 or 2 or 3)
        {
            normalized = "G" + (int)n;
            return true;
        }
        normalized = "";
        return false;
    }

    /// <summary>Splits a line that was already cleaned with <see cref="GCodeProgram.Clean"/> (uppercase, no comments).</summary>
    public static List<GCodeWord> Parse(string cleanedLine) =>
        WordRegex().Matches(cleanedLine).Select(m => new GCodeWord(m.Groups[1].Value[0], m.Groups[2].Value)).ToList();

    [GeneratedRegex(@"([A-Z])\s*([-+]?(?:\d+\.?\d*|\.\d+))")]
    private static partial Regex WordRegex();
}
