using GrblCore;

namespace GrblCore.Tests;

public class JobEstimateTests
{
    // 600 mm/min = 10 mm/s; with 10 mm/s² it takes 1 s and 5 mm to reach full speed.
    static readonly MachineLimits Limits = new(SeekRate: 600, DefaultFeed: 300, Acceleration: 10, JunctionDeviation: 0.05);
    const int FastBaud = 1_000_000; // serial time negligible unless a test is about it

    static JobEstimate Estimate(int baud, params string[] lines)
    {
        var program = GCodeProgram.Parse(lines);
        return JobEstimate.Calculate(program, Toolpath.Build(program), Limits, baud);
    }

    [Fact]
    public void Long_line_accelerates_cruises_and_brakes()
    {
        // 1 s up + 90 mm at 10 mm/s + 1 s down.
        Assert.Equal(11, Estimate(FastBaud, "G1 X100 F600").Total.TotalSeconds, 2);
    }

    [Fact]
    public void Short_line_never_reaches_full_speed()
    {
        // Peak sqrt(10 * 2) mm/s, reached and lost in 2 * 0.447 s.
        Assert.Equal(2 * Math.Sqrt(20) / 10, Estimate(FastBaud, "G1 X2 F600").Total.TotalSeconds, 2);
    }

    [Fact]
    public void Straight_continuation_does_not_slow_down()
    {
        Assert.Equal(11, Estimate(FastBaud, "G1 X50 F600", "X100").Total.TotalSeconds, 2);
    }

    [Fact]
    public void Corner_is_slower_than_straight()
    {
        var corner = Estimate(FastBaud, "G1 X50 F600", "Y50").Total;
        var straight = Estimate(FastBaud, "G1 X50 F600", "X100").Total;
        Assert.True(corner > straight);
    }

    [Fact]
    public void Laser_switch_stops_the_machine()
    {
        var withSwitch = Estimate(FastBaud, "G1 X50 F600", "M3", "X100").Total.TotalSeconds;
        Assert.Equal(12, withSwitch, 2); // two 50 mm moves from standstill to standstill, 1 + 4 + 1 s each
    }

    [Fact]
    public void Rapid_uses_seek_rate_and_missing_feed_uses_default()
    {
        Assert.Equal(11, Estimate(FastBaud, "G0 X100").Total.TotalSeconds, 2);
        // 300 mm/min = 5 mm/s: 0.5 s and 1.25 mm to speed up, 97.5 mm cruise.
        Assert.Equal(0.5 + 97.5 / 5 + 0.5, Estimate(FastBaud, "G1 X100").Total.TotalSeconds, 2);
    }

    [Fact]
    public void Many_tiny_moves_are_limited_by_the_serial_line()
    {
        var lines = Enumerable.Range(1, 100).Select(i => $"G1 X{i * 0.01:0.00} F600").ToArray();
        var estimate = Estimate(9600, lines);
        int bytes = lines.Sum(l => l.Length + 1);
        Assert.True(estimate.Total.TotalSeconds >= bytes / 960.0);
    }

    [Fact]
    public void Remaining_counts_down_with_completed_lines()
    {
        var estimate = Estimate(FastBaud, "M3", "G1 X100 F600", "M5", "G0 X0");

        Assert.Equal(estimate.Total, estimate.Remaining(0));
        Assert.Equal(11, estimate.Remaining(2).TotalSeconds, 2); // only the rapid back is left
        Assert.Equal(TimeSpan.Zero, estimate.Remaining(4));
    }
}
