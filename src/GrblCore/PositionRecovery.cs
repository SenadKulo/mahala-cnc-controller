using System.Globalization;

namespace GrblCore;

/// <summary>What to do about the work position after Grbl was reset.</summary>
/// <param name="Target">Work position Grbl should report again.</param>
/// <param name="MachinePositionLost">
/// True if Grbl restarted (machine position zeroed): restoring assumes the machine wasn't moved meanwhile,
/// so the user must confirm. False if only a work offset was lost: restoring is safe.
/// </param>
public sealed record PositionRestore(Position Target, bool MachinePositionLost);

/// <summary>
/// Work coordinate commands and recovery of the work position after Grbl was reset.
/// </summary>
/// <remarks>
/// The work zero is stored as the G54 offset (G10 L2 P1), which Grbl keeps in EEPROM. A G92 offset
/// would be lost on every soft reset (Ctrl-X), and the program soft-resets Grbl on every connect.
/// Power-up or a USB reset still zeroes the machine position, which is what the remembered position is for.
/// </remarks>
public static class PositionRecovery
{
    /// <summary>Grbl reports 3 decimals; anything closer than this is the same position.</summary>
    public const double Tolerance = 0.002;

    static readonly Position Origin = new(0, 0, 0);

    public static bool Same(Position a, Position b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance && Math.Abs(a.Z - b.Z) <= Tolerance;

    /// <summary>
    /// Compares the last positions seen before the reset with the current ones.
    /// Null when the work position didn't change.
    /// </summary>
    public static PositionRestore? Check(Position savedWork, Position savedMachine, Position currentWork, Position currentMachine)
    {
        bool machinePositionLost = Same(currentMachine, Origin) && !Same(savedMachine, Origin);
        var target = machinePositionLost
            // Grbl restarted: assume the machine stayed where it was last seen.
            ? savedWork
            // Machine position survived: only the work offset is gone. This stays correct
            // even if the last status before the reset was taken while still moving.
            : currentMachine - (savedMachine - savedWork);
        return Same(target, currentWork) ? null : new PositionRestore(target, machinePositionLost);
    }

    /// <summary>
    /// Makes Grbl report <paramref name="work"/> at the current spot: clears any G92 offset and stores
    /// the matching G54 offset. Needs the machine standing still at <paramref name="currentMachine"/>.
    /// </summary>
    public static IReadOnlyList<string> SetWorkPositionCommands(Position work, Position currentMachine)
    {
        var offset = currentMachine - work;
        return ["G92.1", $"G21 G10 L2 P1 X{N(offset.X)} Y{N(offset.Y)} Z{N(offset.Z)}"];
    }

    /// <summary>Makes the current spot the work zero.</summary>
    public static IReadOnlyList<string> SetZeroCommands(Position currentMachine) =>
        SetWorkPositionCommands(Origin, currentMachine);

    /// <summary>
    /// Laser off, then rapid to work zero. Below zero Z rises first; above zero it comes down last,
    /// so X/Y never travel low.
    /// </summary>
    public static IReadOnlyList<string> GoToZeroCommands(Position currentWork)
    {
        if (currentWork.Z < -Tolerance)
            return ["M5", "G90 G0 Z0", "G0 X0 Y0"];
        if (currentWork.Z > Tolerance)
            return ["M5", "G90 G0 X0 Y0", "G0 Z0"];
        return ["M5", "G90 G0 X0 Y0"];
    }

    static string N(double value) => (Math.Round(value, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture);
}
