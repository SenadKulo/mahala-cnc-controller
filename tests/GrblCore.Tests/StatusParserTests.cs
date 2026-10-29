using System.Globalization;
using GrblCore;

namespace GrblCore.Tests;

public class StatusParserTests
{
    [Fact]
    public void Parses_08c_format()
    {
        Assert.True(StatusParser.TryParse("<Cycle,MPos:1.000,-2.500,3.250,WPos:0.500,-1.000,0.000>", out var s));

        Assert.Equal(MachineState.Run, s.State);
        Assert.Equal("Cycle", s.RawState);
        Assert.Equal(new Position(1, -2.5, 3.25), s.MachinePosition);
        Assert.Equal(new Position(0.5, -1, 0), s.WorkPosition);
    }

    [Fact]
    public void Parses_09_format_with_extra_fields()
    {
        Assert.True(StatusParser.TryParse("<Run,MPos:1.000,2.000,3.000,WPos:1.000,2.000,3.000,Buf:3,RX:17>", out var s));

        Assert.Equal(MachineState.Run, s.State);
        Assert.Equal(new Position(1, 2, 3), s.WorkPosition);
    }

    [Fact]
    public void Parses_08a_format_without_state()
    {
        Assert.True(StatusParser.TryParse("MPos:[1.000,2.000,3.000],WPos:[4.000,5.000,6.000]", out var s));

        Assert.Equal(MachineState.Unknown, s.State);
        Assert.Equal(new Position(1, 2, 3), s.MachinePosition);
        Assert.Equal(new Position(4, 5, 6), s.WorkPosition);
    }

    [Fact]
    public void Parses_11_format_and_computes_work_position_from_wco()
    {
        Assert.True(StatusParser.TryParse("<Hold:0|MPos:10.000,20.000,5.000|FS:0,0|WCO:1.000,2.000,3.000>", out var s));

        Assert.Equal(MachineState.Hold, s.State);
        Assert.Equal("Hold:0", s.RawState);
        Assert.Equal(new Position(10, 20, 5), s.MachinePosition);
        Assert.Equal(new Position(9, 18, 2), s.WorkPosition);
    }

    [Fact]
    public void Parses_11_format_with_only_work_position()
    {
        Assert.True(StatusParser.TryParse("<Idle|WPos:1.000,2.000,3.000|FS:0,0>", out var s));

        Assert.Null(s.MachinePosition);
        Assert.Equal(new Position(1, 2, 3), s.WorkPosition);
    }

    [Theory]
    [InlineData("Idle", MachineState.Idle)]
    [InlineData("Queue", MachineState.Queue)]
    [InlineData("Hold", MachineState.Hold)]
    [InlineData("Alarm", MachineState.Alarm)]
    [InlineData("Check Mode", MachineState.Check)]
    [InlineData("Home", MachineState.Home)]
    [InlineData("Whatever", MachineState.Unknown)]
    public void Maps_states(string raw, MachineState expected) => Assert.Equal(expected, StatusParser.ParseState(raw));

    [Fact]
    public void Ignores_regional_decimal_separator()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Bosnian locale uses ',' as decimal separator.
            CultureInfo.CurrentCulture = new CultureInfo("bs-Latn-BA");
            Assert.True(StatusParser.TryParse("<Idle,MPos:1.500,0.000,0.000,WPos:1.500,0.000,0.000>", out var s));
            Assert.Equal(1.5, s.MachinePosition!.Value.X);
            Assert.Equal("X1.500 Y0.000 Z0.000", s.MachinePosition.Value.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("error: Bad number format")]
    [InlineData("Grbl 0.8c ['$' for help]")]
    [InlineData("<Idle,MPos:abc,0.000,0.000>")]
    public void Rejects_non_status_lines(string line) => Assert.False(StatusParser.TryParse(line, out _));
}

public class StatusParser11Tests
{
    [Fact]
    public void Reads_feed_and_speed()
    {
        Assert.True(StatusParser.TryParse("<Run|MPos:1.000,2.000,0.000|FS:600,1000>", out var s));

        Assert.Equal(MachineState.Run, s.State);
        Assert.Equal(600, s.Feed);
        Assert.Equal(1000, s.Speed);
        Assert.Null(s.WorkOffset);
        Assert.Null(s.WorkPosition);
    }

    [Fact]
    public void A_later_report_is_completed_with_the_last_offset()
    {
        Assert.True(StatusParser.TryParse("<Idle|MPos:10.000,20.000,0.000|FS:0,0>", out var s));

        var completed = s.WithOffset(new Position(1, 2, 0));

        Assert.Equal(new Position(10, 20, 0), completed.MachinePosition);
        Assert.Equal(new Position(9, 18, 0), completed.WorkPosition);
    }
}
