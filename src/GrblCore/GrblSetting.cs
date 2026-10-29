using System.Globalization;
using System.Text.RegularExpressions;

namespace GrblCore;

/// <summary>One line of the '$$' dump, e.g. <c>$0=755.906 (x, step/mm)</c> (0.8/0.9) or <c>$0=10</c> (1.1).</summary>
public sealed partial record GrblSetting(int Number, string Value, string? Description)
{
    /// <summary>Command that dumps all settings.</summary>
    public const string DumpCommand = "$$";

    [GeneratedRegex(@"^\$(\d+)\s*=\s*([^\s(]+)\s*(?:\((.*)\))?\s*$")]
    private static partial Regex LinePattern();

    public static bool TryParse(string line, out GrblSetting setting)
    {
        var match = LinePattern().Match(line.Trim());
        if (!match.Success)
        {
            setting = null!;
            return false;
        }
        var description = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
        setting = new GrblSetting(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value, description);
        return true;
    }

    /// <summary>True if the setting holds a whole number (counts, masks, flags) rather than a decimal.</summary>
    public bool IsInteger => !Value.Contains('.');

    /// <summary>
    /// Checks a new value the way Grbl will accept it: a plain non-negative number,
    /// and a whole one where the setting is whole. Returns the value normalized, or null if invalid.
    /// </summary>
    public string? Normalize(string value)
    {
        value = value.Trim().Replace(',', '.');
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return null;
        if (IsInteger && (number != decimal.Truncate(number) || number > 65535))
            return null;
        return IsInteger ? ((int)number).ToString(CultureInfo.InvariantCulture) : number.ToString("0.0#####", CultureInfo.InvariantCulture);
    }

    /// <summary>Command that stores <paramref name="value"/> (already normalized) in Grbl's EEPROM.</summary>
    public string SetCommand(string value) => $"${Number}={value}";
}
