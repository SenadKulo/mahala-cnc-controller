using System.Globalization;
using System.Text.RegularExpressions;

namespace GrblCore;

/// <summary>A cleaned line ready to send. <paramref name="SourceLineNumber"/> is 1-based, in the original file.</summary>
public sealed record GCodeLine(int SourceLineNumber, string Text);

public enum GCodeIssueKind
{
    /// <summary>Line didn't fit Grbl's line buffer; decimals were rounded to 3 places. Warning only.</summary>
    Shortened,
    /// <summary>M2/M30 removed - Grbl 0.8 sometimes locked up on them (Grbl Controller removed them too).</summary>
    ProgramEndRemoved,
    /// <summary>Command Grbl 0.8 doesn't know (G49, M6, T1...) removed; Grbl would reject the whole line.</summary>
    UnsupportedRemoved,
    /// <summary>Line is too long even after shortening. The program can't be run.</summary>
    TooLong,
}

/// <param name="Detail">For <see cref="GCodeIssueKind.UnsupportedRemoved"/>: the removed words, e.g. "G49".</param>
public sealed record GCodeIssue(int SourceLineNumber, GCodeIssueKind Kind, string OriginalText, string? Detail = null);

/// <summary>G-code file cleaned up for Grbl: comments, line numbers and empty lines removed.</summary>
public sealed partial class GCodeProgram
{
    /// <summary>
    /// Grbl 0.8 line buffer. Grbl skips whitespace when filling it, so only non-space characters count.
    /// 50 is Grbl Controller's default; Grbl 0.9+ allows 80.
    /// </summary>
    public const int DefaultMaxLineLength = 50;

    public string? FilePath { get; }
    public IReadOnlyList<GCodeLine> Lines { get; }
    public IReadOnlyList<GCodeIssue> Issues { get; }

    /// <summary>False if any line is too long to send.</summary>
    public bool CanRun => Issues.All(i => i.Kind != GCodeIssueKind.TooLong);

    GCodeProgram(string? filePath, IReadOnlyList<GCodeLine> lines, IReadOnlyList<GCodeIssue> issues)
    {
        FilePath = filePath;
        Lines = lines;
        Issues = issues;
    }

    public static GCodeProgram Load(string path, int maxLineLength = DefaultMaxLineLength) =>
        Parse(File.ReadLines(path), maxLineLength, path);

    public static GCodeProgram Parse(IEnumerable<string> sourceLines, int maxLineLength = DefaultMaxLineLength, string? filePath = null)
    {
        var lines = new List<GCodeLine>();
        var issues = new List<GCodeIssue>();
        int number = 0;

        foreach (var source in sourceLines)
        {
            number++;
            var text = Clean(source);

            if (ProgramEndRegex().IsMatch(text))
            {
                text = Whitespace().Replace(ProgramEndRegex().Replace(text, ""), " ").Trim();
                issues.Add(new GCodeIssue(number, GCodeIssueKind.ProgramEndRemoved, source));
            }
            var removed = new List<string>();
            text = RemoveUnsupported(text, removed);
            if (removed.Count > 0)
                issues.Add(new GCodeIssue(number, GCodeIssueKind.UnsupportedRemoved, source, string.Join(" ", removed)));
            if (text.Length == 0)
                continue;

            if (CountedLength(text) > maxLineLength)
            {
                text = Decimals().Replace(text, m => double.Parse(m.Value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture));
                issues.Add(new GCodeIssue(number, CountedLength(text) > maxLineLength ? GCodeIssueKind.TooLong : GCodeIssueKind.Shortened, source));
            }
            // Spaces only help readability; drop them if the line wouldn't fit the serial buffer otherwise.
            if (text.Length > GrblConnection.MaxLineLength)
                text = Whitespace().Replace(text, "");

            lines.Add(new GCodeLine(number, text));
        }

        return new GCodeProgram(filePath, lines, issues);
    }

    /// <summary>Removes comments, block numbers and '%', uppercases and normalizes whitespace.</summary>
    public static string Clean(string line)
    {
        line = ParenComment().Replace(line, " ");
        int semicolon = line.IndexOf(';');
        if (semicolon >= 0)
            line = line[..semicolon];
        line = line.Replace("%", "").Trim().ToUpperInvariant();
        line = BlockNumber().Replace(line, "");
        return Whitespace().Replace(line, " ").Trim();
    }

    // Commands Grbl 0.8 understands (github.com/grbl/grbl/wiki). M2/M30 are handled separately.
    static readonly HashSet<decimal> SupportedG = [0, 1, 2, 3, 4, 10, 17, 18, 19, 20, 21, 28, 28.1m, 30, 30.1m, 53, 54, 55, 56, 57, 58, 59, 80, 90, 91, 92, 92.1m, 93, 94];
    static readonly HashSet<decimal> SupportedM = [0, 3, 4, 5, 8, 9];
    const string SupportedParameters = "XYZIJKFLPRS";

    /// <summary>
    /// Drops words Grbl 0.8 would reject - one unknown word makes Grbl reject the whole line,
    /// e.g. ArtCAM's header "G90G80G21G49" would lose G90/G21 because of G49.
    /// </summary>
    static string RemoveUnsupported(string text, List<string> removed)
    {
        var words = GCodeWord.Parse(text);
        if (words.Count == 0)
            return text;

        var kept = new List<string>();
        foreach (var w in words)
        {
            bool numberOk = w.TryGetNumber(out var number);
            bool supported = w.Letter switch
            {
                'G' => numberOk && SupportedG.Contains(number),
                'M' => numberOk && SupportedM.Contains(number),
                _ => SupportedParameters.Contains(w.Letter),
            };
            if (supported)
            {
                kept.Add(w.Text);
            }
            else
            {
                removed.Add(w.Text);
                // G43/G44 (tool length offset) take an H word and possibly Z - the rest of the line belongs to them.
                if (w.Letter == 'G' && number is 43 or 44)
                {
                    removed.AddRange(words.Skip(kept.Count + removed.Count).Select(r => r.Text));
                    break;
                }
            }
        }
        // Rejoining without spaces keeps lines like "G90G80G21G49" compact; otherwise keep the original spacing.
        if (removed.Count == 0)
            return text;
        return string.Join(text.Contains(' ') ? " " : "", kept);
    }

    static int CountedLength(string text) => text.Count(c => !char.IsWhiteSpace(c));

    [GeneratedRegex(@"\([^)]*\)?")]
    private static partial Regex ParenComment();

    [GeneratedRegex(@"^N\d+\s*")]
    private static partial Regex BlockNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<![A-Z])M0*(?:2|30)(?![0-9.])")]
    private static partial Regex ProgramEndRegex();

    [GeneratedRegex(@"-?\d*\.\d{4,}")]
    private static partial Regex Decimals();
}
