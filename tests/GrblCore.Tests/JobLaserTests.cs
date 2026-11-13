using GrblCore;

namespace GrblCore.Tests;

public class JobLaserTests
{
    // 0: G0 to the start, 1: M3, 2-3: cut, 4: M5, 5: G0 away, 6: M3, 7: cut back.
    static readonly string[] Lines = ["G0 X10 Y0", "M3", "G1 X10 Y10 F300", "G1 X20 Y10", "M5", "G0 X30 Y0", "M3", "G1 X30 Y20"];

    static JobLaser Make(params string[] lines)
    {
        var program = GCodeProgram.Parse(lines);
        return new JobLaser(program, Toolpath.Build(program));
    }

    [Fact]
    public void Modal_M3_and_M5_per_line()
    {
        var laser = Make(Lines);

        Assert.Equal([false, true, true, true, false, false, true, true], Enumerable.Range(0, Lines.Length).Select(laser.IsOnAfter));
        Assert.False(laser.IsOnAfter(-1));
    }

    [Fact]
    public void The_executing_line_is_found_from_the_position_not_from_the_accepted_lines()
    {
        var laser = Make(Lines);

        // All lines accepted, but the CNC is still on the rapid to the start: dark.
        int line = laser.ExecutingLine(Lines.Length, new Position(5, 0, 0), out var kind);
        Assert.Equal(0, line);
        Assert.Equal(SegmentKind.Rapid, kind);
        Assert.False(laser.IsOnAfter(line));

        line = laser.ExecutingLine(Lines.Length, new Position(10, 4, 0), out kind);
        Assert.Equal(2, line);
        Assert.Equal(SegmentKind.Cut, kind);
        Assert.True(laser.IsOnAfter(line));

        line = laser.ExecutingLine(Lines.Length, new Position(25, 5, 0), out _);
        Assert.Equal(5, line);
        Assert.False(laser.IsOnAfter(line));
    }

    [Fact]
    public void It_never_goes_back_to_an_earlier_line_through_the_same_spot()
    {
        // A square: the last cut ends where the first one started.
        var laser = Make("M3", "G1 X10 F300", "G1 Y10", "G1 X0", "G1 Y0", "M5", "G0 X0 Y-10");

        Assert.Equal(3, laser.ExecutingLine(7, new Position(5, 10, 0), out _));
        Assert.Equal(4, laser.ExecutingLine(7, new Position(0, 0.5, 0), out _));
        // Back at the start corner, which line 1 also touches.
        Assert.Equal(4, laser.ExecutingLine(7, new Position(0, 0, 0), out _));
    }

    [Fact]
    public void Only_accepted_lines_are_candidates()
    {
        var laser = Make(Lines);

        // Lines 0-2 accepted: the CNC can't be on line 5's rapid yet, however near it is.
        Assert.Equal(2, laser.ExecutingLine(3, new Position(25, 5, 0), out _));
        Assert.Equal(-1, laser.ExecutingLine(0, new Position(0, 0, 0), out _));
    }
}
