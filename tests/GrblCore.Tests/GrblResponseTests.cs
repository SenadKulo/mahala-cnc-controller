namespace GrblCore.Tests;

public class GrblResponseTests
{
    [Theory]
    [InlineData("error:22", true, 22)]
    [InlineData("error: Bad number format", false, 0)]
    [InlineData("ok", false, 0)]
    public void Error_codes(string line, bool numbered, int code)
    {
        Assert.Equal(numbered, GrblResponse.TryGetErrorCode(line, out var c));
        Assert.Equal(code, c);
    }

    [Theory]
    [InlineData("ALARM:3", true, 3)]
    [InlineData("ALARM: Abort during cycle", false, 0)]
    public void Alarm_codes(string line, bool numbered, int code)
    {
        Assert.Equal(numbered, GrblResponse.TryGetAlarmCode(line, out var c));
        Assert.Equal(code, c);
    }
}
