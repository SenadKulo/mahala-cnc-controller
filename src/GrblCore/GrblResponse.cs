using System.Globalization;

namespace GrblCore;

/// <summary>
/// Errors and alarms as Grbl prints them. 0.8/0.9 write the reason ("error: Bad number format",
/// "ALARM: Abort during cycle"); 1.1 writes only a number ("error:20", "ALARM:3"), which the UI explains.
/// </summary>
public static class GrblResponse
{
    /// <summary>The number of a 1.1 error ("error:20"); false for 0.8's text errors and other lines.</summary>
    public static bool TryGetErrorCode(string line, out int code) => TryGetCode(line, "error:", out code);

    /// <summary>The number of a 1.1 alarm ("ALARM:3"); false for 0.8's text alarms and other lines.</summary>
    public static bool TryGetAlarmCode(string line, out int code) => TryGetCode(line, "ALARM:", out code);

    static bool TryGetCode(string line, string prefix, out int code)
    {
        line = line.Trim();
        code = 0;
        return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out code);
    }
}
