namespace GrblCore.Tests;

public class CompressorScheduleTests
{
    // The restarts of the night of 26.10.2026 were at 03:49:38, 04:04:38, ...
    static readonly CompressorSchedule Night = new(new DateTime(2026, 10, 26, 3, 49, 37), TimeSpan.FromMinutes(15));

    static DateTime At(int hour, int minute, int second) => new(2026, 10, 26, hour, minute, second);

    [Fact]
    public void NextStart_WholePeriodsFromTheKnownStart()
    {
        Assert.Equal(At(5, 4, 37), Night.NextStart(At(4, 50, 0)));
        Assert.Equal(At(5, 4, 37), Night.NextStart(At(5, 4, 37)));
        Assert.Equal(At(3, 34, 37), Night.NextStart(At(3, 30, 0)));
    }

    [Fact]
    public void UpcomingStart_StaysOnTheStartUntilItsQuietWindowEnds()
    {
        Assert.Equal(At(5, 4, 37), Night.UpcomingStart(At(5, 4, 45)));
        Assert.Equal(At(5, 19, 37), Night.UpcomingStart(At(5, 4, 48)));
    }

    [Fact]
    public void ShouldBreak_WhenTheWorkWouldRunIntoTheWindow()
    {
        var now = At(5, 4, 0);
        Assert.False(Night.ShouldBreak(now, At(5, 4, 26)));
        Assert.True(Night.ShouldBreak(now, At(5, 4, 28)));
        // Inside the window everything waits; after it, nothing does.
        Assert.True(Night.ShouldBreak(At(5, 4, 40), At(5, 4, 40)));
        Assert.False(Night.ShouldBreak(At(5, 4, 48), At(5, 4, 50)));
    }

    [Fact]
    public void BreakEnd_TenSecondsAfterTheStart()
    {
        Assert.Equal(At(5, 4, 47), Night.BreakEnd(At(5, 4, 25)));
    }

    [Fact]
    public void Learn_TakesThePhaseFromTheRestart_KeepsThePeriod()
    {
        var learned = CompressorSchedule.Learn(Night with { Period = TimeSpan.FromMinutes(20) }, At(6, 0, 10));

        Assert.Equal(At(6, 0, 9), learned.KnownStart);
        Assert.Equal(TimeSpan.FromMinutes(20), learned.Period);
        Assert.Equal(CompressorSchedule.DefaultPeriod, CompressorSchedule.Learn(null, At(6, 0, 10)).Period);
    }
}
