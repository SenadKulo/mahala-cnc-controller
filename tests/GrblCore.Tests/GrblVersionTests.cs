using GrblCore;

namespace GrblCore.Tests;

public class GrblVersionTests
{
    [Theory]
    [InlineData("Grbl 0.8c ['$' for help]", 0, 8, 'c')]
    [InlineData("Grbl 0.9j ['$' for help]", 0, 9, 'j')]
    [InlineData("Grbl 1.1h ['$' for help]", 1, 1, 'h')]
    public void Parses_banner(string banner, int major, int minor, char letter)
    {
        Assert.True(GrblVersion.TryParseBanner(banner, out var v));

        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(letter, v.Letter);
        Assert.Equal($"{major}.{minor}{letter}", v.ToString());
    }

    [Fact]
    public void Parses_grblHAL_banner()
    {
        Assert.True(GrblVersion.TryParseBanner("GrblHAL 1.1f ['$' or '$HELP' for help]", out var v));

        Assert.Equal("1.1f", v.ToString());
    }

    [Theory]
    [InlineData("Grbl 0.8c ['$' for help]", false)]
    [InlineData("Grbl 0.9j ['$' for help]", true)]
    [InlineData("Grbl 1.1h ['$' for help]", true)]
    public void Spindle_speed_from_09(string banner, bool expected)
    {
        Assert.True(GrblVersion.TryParseBanner(banner, out var v));
        Assert.Equal(expected, v.UsesSpindleSpeed);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("<Idle,MPos:0.000,0.000,0.000,WPos:0.000,0.000,0.000>")]
    public void Rejects_other_lines(string line) => Assert.False(GrblVersion.TryParseBanner(line, out _));
}
