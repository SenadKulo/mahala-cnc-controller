using GrblCore;

namespace GrblCore.Tests;

public class PositionRecoveryTests
{
    static readonly Position Origin = new(0, 0, 0);

    [Fact]
    public void Lost_offset_is_restored_without_asking()
    {
        // Zero was at machine 10,20,0; laser stopped at work 5,5 (machine 15,25).
        // After Ctrl-X a G92 offset is gone, so the work position equals the machine position.
        var restore = PositionRecovery.Check(
            savedWork: new(5, 5, 0), savedMachine: new(15, 25, 0),
            currentWork: new(15, 25, 0), currentMachine: new(15, 25, 0));

        Assert.Equal(new PositionRestore(new(5, 5, 0), MachinePositionLost: false), restore);
    }

    [Fact]
    public void Reset_while_moving_uses_the_current_machine_position()
    {
        // The last status was taken at work 5,5 but the machine went on to 7,5 before the reset.
        var restore = PositionRecovery.Check(
            savedWork: new(5, 5, 0), savedMachine: new(15, 25, 0),
            currentWork: new(17, 25, 0), currentMachine: new(17, 25, 0));

        Assert.Equal(new Position(7, 5, 0), restore!.Target);
    }

    [Fact]
    public void Restart_zeroes_machine_position_and_needs_confirmation()
    {
        var restore = PositionRecovery.Check(
            savedWork: new(5, 5, 0), savedMachine: new(15, 25, 0),
            currentWork: Origin, currentMachine: Origin);

        Assert.Equal(new PositionRestore(new(5, 5, 0), MachinePositionLost: true), restore);
    }

    [Fact]
    public void Nothing_to_restore_when_the_work_position_is_unchanged()
    {
        Assert.Null(PositionRecovery.Check(
            savedWork: new(5, 5, 0), savedMachine: new(15, 25, 0),
            currentWork: new(5.001, 5, 0), currentMachine: new(15.001, 25, 0)));
    }

    [Fact]
    public void Nothing_to_restore_when_machine_was_and_is_at_origin_without_offset()
    {
        Assert.Null(PositionRecovery.Check(Origin, Origin, Origin, Origin));
    }

    [Fact]
    public void Zero_is_stored_as_g54_offset_at_the_machine_position()
    {
        Assert.Equal(["G92.1", "G21 G10 L2 P1 X15.25 Y-2 Z0"], PositionRecovery.SetZeroCommands(new(15.25, -2, -0.0001)));
    }

    [Fact]
    public void Work_position_offset_is_machine_minus_work_and_culture_invariant()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("bs-Latn-BA");
            // Machine 15.5,25 should read as work 5,5 -> offset 10.5,20.
            Assert.Equal("G21 G10 L2 P1 X10.5 Y20 Z0", PositionRecovery.SetWorkPositionCommands(new(5, 5, 0), new(15.5, 25, 0))[1]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Position_never_shows_minus_zero()
    {
        Assert.Equal("X0.000 Y0.000 Z1.500", new Position(-0.0, -0.0001, 1.5).ToString());
    }

    [Fact]
    public void Go_to_zero_turns_laser_off_and_keeps_xy_moves_high()
    {
        Assert.Equal(["M5", "G90 G0 X0 Y0"], PositionRecovery.GoToZeroCommands(new(10, 10, 0)));
        Assert.Equal(["M5", "G90 G0 Z0", "G0 X0 Y0"], PositionRecovery.GoToZeroCommands(new(10, 10, -2)));
        Assert.Equal(["M5", "G90 G0 X0 Y0", "G0 Z0"], PositionRecovery.GoToZeroCommands(new(10, 10, 5)));
    }
}
