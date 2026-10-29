using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GrblCore;

/// <summary>
/// Parses Grbl status reports in all three formats:
/// 0.8a  <c>MPos:[0.000,0.000,0.000],WPos:[0.000,0.000,0.000]</c>
/// 0.8c/0.9  <c>&lt;Idle,MPos:0.000,0.000,0.000,WPos:0.000,0.000,0.000&gt;</c>
/// 1.1  <c>&lt;Idle|MPos:0.000,0.000,0.000|FS:0,0|WCO:0.000,0.000,0.000&gt;</c>
/// </summary>
public static partial class StatusParser
{
    public static bool TryParse(string line, [NotNullWhen(true)] out GrblStatus? status)
    {
        status = null;
        line = line.Trim();

        if (line.StartsWith('<') && line.EndsWith('>'))
        {
            var body = line[1..^1];
            return body.Contains('|') ? TryParseV11(body, out status) : TryParseV08c(body, out status);
        }
        if (line.StartsWith("MPos:[", StringComparison.Ordinal))
            return TryParseV08a(line, out status);

        return false;
    }

    public static MachineState ParseState(string rawState)
    {
        // 1.1 adds a sub-state after a colon ("Hold:0", "Door:1").
        int colon = rawState.IndexOf(':');
        var name = colon < 0 ? rawState : rawState[..colon];
        return name switch
        {
            "Idle" => MachineState.Idle,
            "Queue" => MachineState.Queue,
            "Cycle" or "Run" => MachineState.Run,
            "Hold" => MachineState.Hold,
            "Home" => MachineState.Home,
            "Alarm" => MachineState.Alarm,
            "Check Mode" or "Check" => MachineState.Check,
            "Door" => MachineState.Door,
            "Jog" => MachineState.Jog,
            "Sleep" => MachineState.Sleep,
            _ => MachineState.Unknown,
        };
    }

    static bool TryParseV08c(string body, out GrblStatus? status)
    {
        status = null;
        var parts = body.Split(',');
        if (!TryReadAxes(parts, "MPos:", out var mpos))
            return false;

        Position? wpos = TryReadAxes(parts, "WPos:", out var w) ? w : null;
        status = new GrblStatus(ParseState(parts[0]), parts[0], mpos, wpos);
        return true;
    }

    static bool TryParseV11(string body, out GrblStatus? status)
    {
        status = null;
        var fields = body.Split('|');
        Position? mpos = null, wpos = null, wco = null;
        double? feed = null, speed = null;

        foreach (var field in fields.Skip(1))
        {
            int colon = field.IndexOf(':');
            if (colon < 0)
                continue;
            var values = field[(colon + 1)..].Split(',');
            var name = field[..colon];
            switch (name)
            {
                case "MPos" or "WPos" or "WCO":
                    if (values.Length < 3 || !TryPosition(values[0], values[1], values[2], out var p))
                        break;
                    if (name == "MPos") mpos = p;
                    else if (name == "WPos") wpos = p;
                    else wco = p;
                    break;
                case "FS" when values.Length >= 2:
                    feed = Number(values[0]);
                    speed = Number(values[1]);
                    break;
                case "F":
                    feed = Number(values[0]);
                    break;
            }
        }

        if (mpos is null && wpos is null)
            return false;

        status = new GrblStatus(ParseState(fields[0]), fields[0], mpos, wpos) { WorkOffset = wco, Feed = feed, Speed = speed };
        // 1.1 sends either MPos or WPos, and the offset between them (WCO) only now and then;
        // GrblConnection completes the other reports from the last offset.
        if (wco is { } offset)
            status = status.WithOffset(offset);
        return true;
    }

    static bool TryParseV08a(string line, out GrblStatus? status)
    {
        status = null;
        var m = V08aRegex().Match(line);
        if (!m.Success || !TryPosition(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var mpos))
            return false;

        Position? wpos = m.Groups[4].Success && TryPosition(m.Groups[4].Value, m.Groups[5].Value, m.Groups[6].Value, out var w) ? w : null;
        status = new GrblStatus(MachineState.Unknown, "", mpos, wpos);
        return true;
    }

    static bool TryReadAxes(string[] parts, string prefix, out Position pos)
    {
        pos = default;
        int i = Array.FindIndex(parts, p => p.StartsWith(prefix, StringComparison.Ordinal));
        if (i < 0 || i + 2 >= parts.Length)
            return false;
        return TryPosition(parts[i][prefix.Length..], parts[i + 1], parts[i + 2], out pos);
    }

    static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    static bool TryPosition(string x, string y, string z, out Position pos)
    {
        // Grbl always uses '.' as decimal separator, regardless of the PC's regional settings.
        const NumberStyles style = NumberStyles.Float;
        var inv = CultureInfo.InvariantCulture;
        if (double.TryParse(x, style, inv, out var px) && double.TryParse(y, style, inv, out var py) && double.TryParse(z, style, inv, out var pz))
        {
            pos = new Position(px, py, pz);
            return true;
        }
        pos = default;
        return false;
    }

    [GeneratedRegex(@"MPos:\[([^,\]]+),([^,\]]+),([^,\]]+)\](?:,WPos:\[([^,\]]+),([^,\]]+),([^,\]]+)\])?")]
    private static partial Regex V08aRegex();
}
