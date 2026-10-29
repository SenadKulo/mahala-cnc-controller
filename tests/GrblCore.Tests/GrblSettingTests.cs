using GrblCore;

namespace GrblCore.Tests;

public class GrblSettingTests
{
    [Theory]
    [InlineData("$0=755.906 (x, step/mm)", 0, "755.906", "x, step/mm")]
    [InlineData("$6=28 (step port invert mask, int:00011100)", 6, "28", "step port invert mask, int:00011100")]
    [InlineData("$110=500.000", 110, "500.000", null)]
    [InlineData("$13=0 (report inches, bool)", 13, "0", "report inches, bool")]
    public void Parses_dump_lines(string line, int number, string value, string? description)
    {
        Assert.True(GrblSetting.TryParse(line, out var setting));
        Assert.Equal(new GrblSetting(number, value, description), setting);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("'$x=value' to set parameter or just '$' to dump current settings")]
    [InlineData("$N0=")]
    [InlineData("<Idle,MPos:0.000,0.000,0.000,WPos:0.000,0.000,0.000>")]
    public void Ignores_other_lines(string line)
    {
        Assert.False(GrblSetting.TryParse(line, out _));
    }

    [Fact]
    public void Decimal_setting_accepts_comma_and_keeps_decimals()
    {
        var setting = new GrblSetting(8, "50.000", null);

        Assert.Equal("12.5", setting.Normalize("12,5"));
        Assert.Equal("100.0", setting.Normalize(" 100 "));
        Assert.Null(setting.Normalize("-1"));
        Assert.Null(setting.Normalize("abc"));
    }

    [Fact]
    public void Integer_setting_rejects_fractions()
    {
        var setting = new GrblSetting(7, "25", null);

        Assert.Equal("255", setting.Normalize("255"));
        Assert.Null(setting.Normalize("2.5"));
        Assert.Null(setting.Normalize("70000"));
    }

    [Fact]
    public void Set_command()
    {
        Assert.Equal("$4=400.0", new GrblSetting(4, "500.000", null).SetCommand("400.0"));
    }
}
