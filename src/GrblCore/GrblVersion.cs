using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GrblCore;

/// <summary>
/// Firmware version from the startup banner, e.g. "Grbl 0.8c ['$' for help]", "Grbl 1.1h ['$' for help]"
/// or grblHAL's "GrblHAL 1.1f ['$' or '$HELP' for help]".
/// </summary>
public sealed partial record GrblVersion(int Major, int Minor, char? Letter, string Banner)
{
    public static bool TryParseBanner(string line, [NotNullWhen(true)] out GrblVersion? version)
    {
        var m = BannerRegex().Match(line.Trim());
        if (!m.Success)
        {
            version = null;
            return false;
        }
        char? letter = m.Groups[3].Length > 0 ? char.ToLowerInvariant(m.Groups[3].Value[0]) : null;
        version = new GrblVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), letter, line.Trim());
        return true;
    }

    public bool IsAtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);

    /// <summary>
    /// 0.9 and later: settings numbered $100... for the axes, and M3 runs the spindle/laser at the S value
    /// (S0 after a reset, so M3 alone leaves the laser dark). 0.8 has its own numbering and switches M3 on and off only.
    /// </summary>
    public bool UsesSpindleSpeed => IsAtLeast(0, 9);

    public override string ToString() => $"{Major}.{Minor}{Letter}";

    [GeneratedRegex(@"^Grbl(?:HAL)?\s+(\d+)\.(\d+)([a-z]?)", RegexOptions.IgnoreCase)]
    private static partial Regex BannerRegex();
}
