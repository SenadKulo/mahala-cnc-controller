namespace GrblCore;

public enum MachineState
{
    Unknown,
    Idle,
    Queue,
    /// <summary>Moving. Grbl 0.8c reports "Cycle", 0.9+ reports "Run".</summary>
    Run,
    Hold,
    Home,
    Alarm,
    Check,
    Door,
    Jog,
    Sleep,
}

/// <summary>One status report (answer to '?').</summary>
/// <param name="RawState">State text exactly as Grbl sent it, e.g. "Cycle" or "Hold:0".</param>
public sealed record GrblStatus(MachineState State, string RawState, Position? MachinePosition, Position? WorkPosition)
{
    /// <summary>Work coordinate offset (machine minus work position), when the report carried it (1.1 "WCO:").</summary>
    public Position? WorkOffset { get; init; }

    /// <summary>Current feed in mm/min (1.1 "FS:" or "F:"); null on older versions.</summary>
    public double? Feed { get; init; }

    /// <summary>Current spindle speed / laser power S (1.1 "FS:"); null on older versions.</summary>
    public double? Speed { get; init; }

    /// <summary>
    /// Fills in the position the report left out, from a work offset an earlier report carried.
    /// Grbl 1.1 sends either MPos or WPos, and WCO only every 10-30 reports or after the offset changed.
    /// </summary>
    public GrblStatus WithOffset(Position offset) => this with
    {
        MachinePosition = MachinePosition ?? WorkPosition + offset,
        WorkPosition = WorkPosition ?? MachinePosition - offset,
    };
}
